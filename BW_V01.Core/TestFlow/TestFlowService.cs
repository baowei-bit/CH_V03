using System.Globalization;
using BW_V01.Core.Configuration;

namespace BW_V01.Core.TestFlow;

public sealed class TestFlowService
{
    private readonly ITestProtocolAdapter _protocolAdapter;
    private readonly List<TestItemResult> _items = new();
    private TestFlowState _state = TestFlowState.Idle;
    private string _barcode = string.Empty;
    private string _message = "等待初始化";
    private string _firstFailItem = string.Empty;
    private string _firstFailValue = string.Empty;
    private bool? _isPass;
    private DateTime? _startTime;
    private DateTime? _endTime;

    public TestFlowService(ITestProtocolAdapter? protocolAdapter = null)
    {
        _protocolAdapter = protocolAdapter ?? new DefaultIctProtocolAdapter();
    }

    public TestFlowSnapshot Initialize(TestFlowOptions options)
    {
        _state = options.AutoScan ? TestFlowState.WaitingScan : TestFlowState.Ready;
        _barcode = string.Empty;
        _message = options.AutoScan ? "等待扫码" : "手动/不扫码模式已就绪";
        _firstFailItem = string.Empty;
        _firstFailValue = string.Empty;
        _isPass = null;
        _startTime = null;
        _endTime = null;
        ResetItemValues();
        return Snapshot();
    }

    public TestFlowSnapshot AcceptBarcode(string barcode, TestFlowOptions options)
    {
        barcode = (barcode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return Fail("条码为空");
        }

        if (string.Equals(barcode, "NoRead", StringComparison.OrdinalIgnoreCase))
        {
            return Fail("读码失败：NoRead");
        }

        if (int.TryParse(options.BarcodeLength, out var expectedLength) && expectedLength > 0 && barcode.Length != expectedLength)
        {
            return Fail($"条码长度错误：{barcode.Length}/{expectedLength}");
        }

        if (!string.IsNullOrWhiteSpace(options.CodeSuffix) && !barcode.Contains(options.CodeSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return Fail($"条码缺少固定校验码：{options.CodeSuffix}");
        }

        _barcode = barcode;
        _state = TestFlowState.Ready;
        _message = $"条码校验通过：{barcode}";
        return Snapshot();
    }

    public TestFlowSnapshot StartTest()
    {
        _state = TestFlowState.Testing;
        _message = $"产品 {_barcode} 测试开始";
        _firstFailItem = string.Empty;
        _firstFailValue = string.Empty;
        _isPass = null;
        _startTime = DateTime.Now;
        _endTime = null;
        ResetItemValues();
        return Snapshot();
    }

    public TestFlowSnapshot LoadTestItems(string payload)
    {
        _items.Clear();
        foreach (var item in _protocolAdapter.ParseItems(payload))
        {
            _items.Add(item);
        }

        _message = _items.Count == 0 ? "未解析到测试项目" : $"测试项目加载完成：{_items.Count} 项";
        return Snapshot();
    }

    public TestFlowSnapshot LoadConfiguredTestItems(IEnumerable<TestItemConfiguration> items)
    {
        _items.Clear();
        foreach (var item in items.OrderBy(item => item.Order))
        {
            _items.Add(new TestItemResult
            {
                Index = _items.Count + 1,
                ItemName = item.ItemName,
                UpperLimit = item.UpperLimit,
                LowerLimit = item.LowerLimit,
                Unit = item.Unit,
                PointA = item.PointA,
                PointB = item.PointB
            });
        }

        _message = _items.Count == 0 ? "未配置测试项目" : $"已加载配置测试项：{_items.Count} 项";
        return Snapshot();
    }

    public TestFlowSnapshot CompleteTest(string payload)
    {
        if (_state != TestFlowState.Testing)
        {
            StartTest();
        }

        var values = _protocolAdapter.ParseResults(payload).ToList();
        if (_items.Count == 0)
        {
            foreach (var value in values)
            {
                var item = new TestItemResult
                {
                    Index = value.Index,
                    ItemName = value.ItemName,
                    TestValue = value.Value,
                    Result = value.Result
                };
                item.Result = EvaluateResult(item, value.Result);
                _items.Add(item);
            }
        }
        else
        {
            foreach (var value in values)
            {
                var index = value.Index - 1;
                if (index < 0 || index >= _items.Count)
                {
                    continue;
                }

                _items[index].TestValue = value.Value;
                if (!string.IsNullOrWhiteSpace(value.PointA))
                {
                    _items[index].PointA = value.PointA;
                }

                if (!string.IsNullOrWhiteSpace(value.PointB))
                {
                    _items[index].PointB = value.PointB;
                }

                _items[index].Result = EvaluateResult(_items[index], value.Result);
            }
        }

        var failedItem = _items.FirstOrDefault(item => !item.IsPass);
        _isPass = failedItem is null;
        _firstFailItem = failedItem?.ItemName ?? string.Empty;
        _firstFailValue = failedItem?.TestValue ?? string.Empty;
        _endTime = DateTime.Now;
        _state = _isPass == true ? TestFlowState.Completed : TestFlowState.Failed;
        _message = _isPass == true ? "测试结束：PASS" : $"测试结束：FAIL，首个不良项 {_firstFailItem}";
        return Snapshot();
    }

    public TestFlowSnapshot Snapshot()
    {
        return new TestFlowSnapshot
        {
            State = _state,
            Barcode = _barcode,
            Message = _message,
            FirstFailItem = _firstFailItem,
            FirstFailValue = _firstFailValue,
            IsPass = _isPass,
            StartTime = _startTime,
            EndTime = _endTime,
            Items = _items.Select(CloneItem).ToList()
        };
    }

    private TestFlowSnapshot Fail(string message)
    {
        _state = TestFlowState.Failed;
        _message = message;
        _isPass = false;
        _endTime = DateTime.Now;
        return Snapshot();
    }

    private void ResetItemValues()
    {
        foreach (var item in _items)
        {
            item.TestValue = string.Empty;
            item.Result = "WAIT";
        }
    }

    private static TestItemResult CloneItem(TestItemResult item)
    {
        return new TestItemResult
        {
            Index = item.Index,
            ItemName = item.ItemName,
            PointA = item.PointA,
            PointB = item.PointB,
            UpperLimit = item.UpperLimit,
            LowerLimit = item.LowerLimit,
            Unit = item.Unit,
            TestValue = item.TestValue,
            Result = item.Result
        };
    }

    private static string EvaluateResult(TestItemResult item, string fallbackResult)
    {
        var testValue = item.TestValue?.Trim() ?? string.Empty;
        var lowerLimit = item.LowerLimit?.Trim() ?? string.Empty;
        var upperLimit = item.UpperLimit?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(testValue))
        {
            return "WAIT";
        }

        if (TryParseDecimal(testValue, out var value))
        {
            var hasLower = TryParseDecimal(lowerLimit, out var lower);
            var hasUpper = TryParseDecimal(upperLimit, out var upper);
            if (hasLower && value < lower)
            {
                return "NG";
            }

            if (hasUpper && value > upper)
            {
                return "NG";
            }

            if (hasLower || hasUpper)
            {
                return "OK";
            }
        }

        if (!string.IsNullOrWhiteSpace(lowerLimit)
            && string.Equals(lowerLimit, upperLimit, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(testValue, lowerLimit, StringComparison.OrdinalIgnoreCase) ? "OK" : "NG";
        }

        return string.IsNullOrWhiteSpace(fallbackResult) ? "WAIT" : fallbackResult;
    }

    private static bool TryParseDecimal(string text, out decimal value)
    {
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || decimal.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }
}

