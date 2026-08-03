using System.Text.Json;
using CH_V01.Core.Mes;
using CareerRule;

namespace CH_V01.Mes.JiaLianYiCareerRule;

public sealed class JiaLianYiCareerRuleMesPlugin : IMesPlugin
{
    public string Id => "JiaLianYi.CareerRule";

    public string Name => "嘉联益 CareerRule MES";

    public MesPluginCapabilities Capabilities { get; } = new()
    {
        SupportsConnectionTest = true,
        SupportsPreCheck = true,
        SupportsResultUpload = true,
        SupportsYieldControl = true
    };

    public IReadOnlyList<MesPluginConfigField> GetConfigFields()
    {
        return new[]
        {
            Field("PartNumber", "产品料号", true),
            Field("Station", "测试站别", true),
            Field("ModifyUser", "测试人员", true),
            Field("Mechine", "设备", true),
            Field("Tooling", "治具", true),
            Field("JobNo", "工单", true),
            Field("OperationSeq", "制程序号", true),
            Field("ItemCode", "品号", true),
            Field("Unit", "单位", true, "pcs"),
            Field("Module", "模块", true, "1")
        };
    }

    public Task<MesResult> TestConnectionAsync(MesPluginOptions options, CancellationToken cancellationToken = default)
    {
        return InvokeAsync(() =>
        {
            var result = Rule.checkAPILink();
            return ConvertResult(result, "MES 连接正常", "MES 连接失败");
        }, cancellationToken);
    }

    public Task<MesResult> LoginAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(MesResult.Success("CareerRule 协议不需要单独登录"));
    }

    public Task<MesResult> CheckBeforeTestAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        return InvokeAsync(() =>
        {
            var station = Value(options, "Station", request.StationNo);
            var jobNo = Value(options, "JobNo", request.ProdNo);
            var result = Rule.checkFuntionTestMachineWork(
                request.Barcode,
                Value(options, "Module"),
                Value(options, "Mechine"),
                Value(options, "Tooling"),
                station,
                Value(options, "OperationSeq"),
                jobNo,
                Value(options, "ItemCode"),
                Value(options, "Unit", "pcs"));

            return ConvertResult(result, "MES 卡关通过", "MES 卡关失败");
        }, cancellationToken);
    }

    public Task<MesResult> UploadResultAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken = default)
    {
        return InvokeAsync(() =>
        {
            var testDataJson = JsonSerializer.Serialize(request.TestItems.Select((item, index) => new TestData
            {
                Item = item.Name,
                USL = item.UpperLimit,
                LSL = item.LowerLimit,
                Unit = item.Unit,
                Value = item.Value,
                Sort = (index + 1).ToString(),
                Remark = string.Empty,
                Flag = IsPass(item.Result) ? "Y" : "N"
            }));

            var resultFlag = string.Equals(request.Result, "PASS", StringComparison.OrdinalIgnoreCase) ? "Y" : "N";
            var station = Value(options, "Station", request.StationNo);
            var jobNo = Value(options, "JobNo", request.ProdNo);
            var defect = resultFlag == "Y" ? string.Empty : request.Remark;
            var startTime = request.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            var endTime = request.EndTime.ToString("yyyy-MM-dd HH:mm:ss");

            var result = Rule.insertFuntionTestData(
                request.Barcode,
                station,
                resultFlag,
                startTime,
                endTime,
                Value(options, "ModifyUser", request.UserNo),
                Value(options, "Mechine"),
                Value(options, "Tooling", request.PackNo),
                jobNo,
                defect,
                Value(options, "OperationSeq"),
                Value(options, "ItemCode"),
                Value(options, "Unit", "pcs"),
                Value(options, "Module"),
                Value(options, "SwVersion"),
                testDataJson);

            return ConvertResult(result, "MES 上传成功", "MES 上传失败");
        }, cancellationToken);
    }

    private static MesPluginConfigField Field(string key, string displayName, bool required, string defaultValue = "")
    {
        return new MesPluginConfigField
        {
            Key = key,
            DisplayName = displayName,
            IsRequired = required,
            DefaultValue = defaultValue
        };
    }

    private static string Value(MesPluginOptions options, string key, string defaultValue = "")
    {
        return options.Get(key, defaultValue);
    }

    private static bool IsPass(string result)
    {
        return string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result, "Y", StringComparison.OrdinalIgnoreCase);
    }

    private static MesResult ConvertResult(RuleResult? result, string successPrefix, string failPrefix)
    {
        if (result is null)
        {
            return MesResult.Fail($"{failPrefix}：接口无返回");
        }

        var message = $"{result.Message};{result.Command}";
        return result.Result
            ? MesResult.Success($"{successPrefix}：{message}")
            : MesResult.Fail($"{failPrefix}：{message}");
    }

    private static async Task<MesResult> InvokeAsync(Func<MesResult> action, CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(action, cancellationToken);
        }
        catch (Exception ex)
        {
            return MesResult.Fail(ex.Message);
        }
    }
}
