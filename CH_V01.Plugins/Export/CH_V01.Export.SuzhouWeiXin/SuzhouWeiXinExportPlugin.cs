using System.Text;
using CH_V01.Core.ResultExport;

namespace CH_V01.Export.SuzhouWeiXin;

public sealed class SuzhouWeiXinExportPlugin : IResultExportPlugin
{
    public string Id => "SuzhouWeiXin.Export";

    public string Name => "苏州维信 CSV 导出";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields()
    {
        return new[]
        {
            Field("CsvDirectory", "CSV目录"),
            Field("ProName", "产品名称"),
            Field("FixtureNo", "治具编号"),
            Field("IpAddress", "机台IP"),
            Field("TestType", "测试类型", "ICT"),
            Field("TesterId", "测试人员/治具"),
            Field("Config", "配置/料号"),
            Field("Holder", "Holder", "A"),
            Field("Slot", "Slot", "1"),
            Field("SwVersion", "软件版本")
        };
    }

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.Combine(context.GetOption("CsvDirectory", context.Configuration.Output.CsvDirectory), DateTime.Now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(directory);

            var filePath = Path.Combine(directory, BuildFileName(context));
            var exists = File.Exists(filePath);
            await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);

            if (!exists)
            {
                await writer.WriteLineAsync(BuildHeader(context));
                await writer.WriteLineAsync(BuildLowerLimitLine(context));
                await writer.WriteLineAsync(BuildUpperLimitLine(context));
                await writer.WriteLineAsync(BuildUnitLine(context));
            }

            await writer.WriteLineAsync(BuildResultLine(context));
            return ResultExportResult.Success(Name, "苏州维信 CSV 导出完成", filePath);
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"苏州维信 CSV 导出失败：{ex.Message}");
        }
    }

    private static string BuildFileName(ResultExportContext context)
    {
        var now = DateTime.Now;
        var proName = context.GetOption("ProName", context.Configuration.Identity.ProductName);
        var fixture = context.GetOption("FixtureNo", context.Configuration.User.FixtureNo);
        var ipAddress = context.GetOption("IpAddress", context.Configuration.Identity.MachineNo);
        var testType = context.GetOption("TestType", context.Configuration.Identity.TestType);
        return ResultExportFileName.ApplyTestPieceSuffix(context, $"{Safe(proName)}_{Safe(fixture)}_{Safe(ipAddress)}_{Safe(testType)}_WTB_{now:yyyyMMdd}_{GetShiftName(now)}.csv");
    }

    private static string BuildHeader(ResultExportContext context)
    {
        var fixedHeaders = new[]
        {
            "SerialNumber", "Test Pass/Fail Status", "errCode", "errStr", "TesterID", "config", "timeStamp",
            "StartTime", "EndTime", "TestTime", "Failing items", "errValue", "holder", "slot", "testModel",
            "swversion", "attribute1", "attribute2", "attribute3", "attribute4"
        };

        return Join(fixedHeaders.Concat(context.Snapshot.Items.Select(item => item.ItemName)));
    }

    private static string BuildLowerLimitLine(ResultExportContext context)
    {
        return "Lower Limit----->," + Blank(19) + Join(context.Snapshot.Items.Select(item => item.LowerLimit));
    }

    private static string BuildUpperLimitLine(ResultExportContext context)
    {
        return "Upper Limit----->," + Blank(19) + Join(context.Snapshot.Items.Select(item => item.UpperLimit));
    }

    private static string BuildUnitLine(ResultExportContext context)
    {
        return "Measurement Unit----->," + Blank(19) + Join(context.Snapshot.Items.Select(item => item.Unit));
    }

    private static string BuildResultLine(ResultExportContext context)
    {
        var values = new List<string>
        {
            context.Snapshot.Barcode,
            context.ResultText,
            context.FirstFailItem,
            string.Empty,
            context.GetOption("TesterId", context.Configuration.User.FixtureNo),
            context.GetOption("Config"),
            string.Empty,
            context.StartTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.EndTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.TestSeconds.ToString("F2"),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.ItemName)),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.TestValue)),
            context.GetOption("Holder", "A"),
            context.GetOption("Slot", "1"),
            context.GetOption("TestType", context.Configuration.Identity.TestType),
            context.GetOption("SwVersion", context.Configuration.Identity.TestProgram),
            context.Guid,
            string.Empty,
            string.Empty,
            string.Empty
        };

        values.AddRange(context.Snapshot.Items.Select(item => item.TestValue));
        return Join(values);
    }

    private static string GetShiftName(DateTime dateTime)
    {
        return dateTime.Hour >= 7 && dateTime.Hour < 19 ? "D" : "N";
    }

    private static ExportPluginConfigField Field(string key, string displayName, string defaultValue = "")
    {
        return new ExportPluginConfigField
        {
            Key = key,
            DisplayName = displayName,
            DefaultValue = defaultValue
        };
    }

    private static string Blank(int count)
    {
        return string.Concat(Enumerable.Repeat(",", count));
    }

    private static string Join(IEnumerable<string> values)
    {
        return string.Join(',', values.Select(Escape));
    }

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string Safe(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "NA" : value;
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }
}
