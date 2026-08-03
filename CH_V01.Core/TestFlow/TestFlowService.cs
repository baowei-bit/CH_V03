using System.Globalization;
using CH_V01.Core.Configuration;

namespace CH_V01.Core.TestFlow;

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
        _state = options.NoScanMode ? TestFlowState.Ready : TestFlowState.WaitingScan;
        _barcode = string.Empty;
        _message = options.NoScanMode
            ? "不扫码模式已就绪"
            : options.AutoScan && !options.ManualScanMode ? "等待自动扫码" : "等待手动扫码";
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

        if (options.ScanRuleEnabled)
        {
            var prefix = options.ScanRulePrefix?.Trim() ?? string.Empty;
            if (prefix.Length > 18)
            {
                return Fail("扫码规则前缀长度不能超过 18 位");
            }

            if (!string.IsNullOrWhiteSpace(prefix) && !barcode.StartsWith(prefix, StringComparison.Ordinal))
            {
                return Fail($"条码不符合扫码规则前缀：{prefix}");
            }
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
        var parsedItems = _protocolAdapter.ParseItems(payload)
            .OrderBy(item => item.Index)
            .ToList();

        if (parsedItems.Count == 0
            || parsedItems.Select((item, index) => item.Index != index + 1).Any(isInvalid => isInvalid))
        {
            _message = "测试项报文校验失败，已保留原测试项目";
            return Snapshot();
        }

        _items.Clear();
        _items.AddRange(parsedItems);
        _message = $"测试项目加载完成：{_items.Count} 项";
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

    public TestFlowSnapshot UpdateItemLimits(int index, string lowerLimit, string upperLimit)
    {
        if (index <= 0 || index > _items.Count)
        {
            return Snapshot();
        }

        var item = _items[index - 1];
        item.LowerLimit = lowerLimit;
        item.UpperLimit = upperLimit;
        _message = $"第 {index} 项限值已调整为 {lowerLimit}~{upperLimit}";
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
                    ItemName = string.IsNullOrWhiteSpace(value.ItemName) ? $"Test{value.Index}" : value.ItemName,
                    TestValue = value.Value,
                    PointA = value.PointA,
                    PointB = value.PointB,
                    Result = value.Result
                };
                item.Result = GetLowerMachineResult(value.Result);
                _items.Add(item);
            }
        }
        else
        {
            foreach (var value in values)
            {
                var index = value.Index - 1;
                if (index < 0)
                {
                    continue;
                }

                while (index >= _items.Count)
                {
                    _items.Add(new TestItemResult
                    {
                        Index = _items.Count + 1,
                        ItemName = $"Test{_items.Count + 1}"
                    });
                }

                if (!string.IsNullOrWhiteSpace(value.ItemName))
                {
                    _items[index].ItemName = value.ItemName;
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

                _items[index].Result = GetLowerMachineResult(value.Result);
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

    /// <summary>
    /// 测试项判定由下位机负责；上位机仅显示并汇总下位机返回的结果，不再依据配置限值复判。
    /// </summary>
    private static string GetLowerMachineResult(string result)
    {
        return string.IsNullOrWhiteSpace(result) ? "WAIT" : result.Trim();
    }
}
