using System.Text;
using CH_V03.Core.ResultExport;
using CH_V03.Core.TestFlow;

namespace CH_V03.Export.SuzhouJiaLianYiLuEnZe;

public sealed class SuzhouJiaLianYiLuEnZeExportPlugin : IResultExportPlugin
{
    public string Id => "SuzhouJiaLianYi.LuEnZe.Export";

    public string Name => "苏州嘉联益（卢恩泽）CSV/DCL 导出";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields()
    {
        return new[]
        {
            Field("CsvDirectory", "CSV目录"),
            new ExportPluginConfigField { Key = SpecialTestValue.OptionKey, DisplayName = "特殊项测试值隐藏", DefaultValue = "false", IsCheckbox = true },
            Field("DclDirectory", "DCL目录"),
            Field("CsvName", "CSV文件前缀"),
            Field("TesterId", "测试员/治具字段"),
            Field("Config", "Config字段", "111"),
            Field("Holder", "Holder", "A"),
            Field("Slot", "Slot", "1"),
            Field("TestType", "测试类型", "ICT"),
            Field("SwVersion", "软件版本")
        };
    }

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var csvPath = await ExportCsvAsync(context, cancellationToken);
            var dclPath = await ExportDclAsync(context, cancellationToken);
            return ResultExportResult.Success(Name, "苏州嘉联益（卢恩泽）导出完成", $"{csvPath}; {dclPath}");
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"苏州嘉联益（卢恩泽）导出失败：{ex.Message}");
        }
    }

    private static async Task<string> ExportCsvAsync(ResultExportContext context, CancellationToken cancellationToken)
    {
        var directory = context.GetOption("CsvDirectory", context.Configuration.Output.CsvDirectory);
        Directory.CreateDirectory(directory);

        var csvName = context.GetOption("CsvName", context.Configuration.Output.CsvName);
        var filePath = Path.Combine(directory, ResultExportFileName.ApplyTestPieceSuffix(context, $"{csvName}_{DateTime.Now:yyyyMMdd}.csv"));
        var exists = File.Exists(filePath);

        await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        if (!exists)
        {
            await writer.WriteLineAsync(BuildCsvHeader(context));
            await writer.WriteLineAsync(BuildLimitLine("Upper Limits-->", context.Snapshot.Items.Select(item => WithUnit(item.UpperLimit, item.Unit))));
            await writer.WriteLineAsync(BuildLimitLine("Lower Limits-->", context.Snapshot.Items.Select(item => WithUnit(item.LowerLimit, item.Unit))));
            await writer.WriteLineAsync(BuildLimitLine("Measurement Unit-->", context.Snapshot.Items.Select(item => item.Unit)));
        }

        await writer.WriteLineAsync(BuildCsvResultLine(context));
        return filePath;
    }

    private static async Task<string> ExportDclAsync(ResultExportContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Snapshot.Barcode))
        {
            throw new InvalidOperationException("条码为空，无法生成 DCL");
        }

        var directory = context.GetOption("DclDirectory", context.Configuration.Output.DclDirectory);
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, ResultExportFileName.ApplyTestPieceSuffix(context, MakeValidFileName(context.Snapshot.Barcode) + ".dcl"));
        await File.WriteAllTextAsync(filePath, BuildDcl(context), Encoding.Default, cancellationToken);
        return filePath;
    }

    private static string BuildCsvHeader(ResultExportContext context)
    {
        var fixedHeaders = new[]
        {
            "SerialNumber", "Test Pass/Fail Status", "errCode", "errStr", "TesterID", "config", "timeStamp",
            "StartTime", "EndTime", "TestTime", "Failing items", "errValue", "holder", "slot", "testModel",
            "swversion", "attribute1", "attribute2", "attribute3", "attribute4"
        };

        return Join(fixedHeaders.Concat(context.Snapshot.Items.Select(item => item.ItemName)));
    }

    private static string BuildLimitLine(string title, IEnumerable<string> values)
    {
        return title + "," + Blank(19) + Join(values);
    }

    private static string BuildCsvResultLine(ResultExportContext context)
    {
        var values = new List<string>
        {
            context.Snapshot.Barcode,
            context.ResultText,
            context.FirstFailItem,
            string.Empty,
            context.GetOption("TesterId", context.Configuration.Mes.Tooling),
            context.GetOption("Config", "111"),
            string.Empty,
            context.StartTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.EndTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.TestSeconds.ToString("F2"),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.ItemName)),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => SpecialTestValue.CsvValue(context, item))),
            context.GetOption("Holder", "A"),
            context.GetOption("Slot", "1"),
            context.GetOption("TestType", context.Configuration.Identity.TestType),
            context.GetOption("SwVersion", context.Configuration.Identity.TestProgram),
            context.Guid,
            string.Empty,
            string.Empty,
            string.Empty
        };

        values.AddRange(context.Snapshot.Items.Select(item => SpecialTestValue.CsvValue(context, item)));
        return Join(values);
    }

    private static string BuildDcl(ResultExportContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// Header_Data");
        builder.AppendLine("// PASS/FAIL, ICT_ID, SeriesNo, BoardName, BarCode, Date, Time, Result, Retest,WorkSchedule,OperID, DetailResult, MulBrdNum, MulBrdResult,RetryCnt,BeginDate,BeginTime,Version,BoardVersion,FixtureBarcode,PinOffset,LeftRight");
        builder.AppendLine(Join(new[]
        {
            context.ResultText,
            context.GetOption("TestType", context.Configuration.Identity.TestType),
            "1",
            context.Configuration.Identity.ProductName,
            context.Snapshot.Barcode,
            DateTime.Now.ToString("yyyyMMdd"),
            DateTime.Now.ToString("HHmmss"),
            context.Snapshot.IsPass == true ? "P" : "F",
            "0",
            "1",
            context.Configuration.User.LoginJobNo,
            string.Concat(context.Snapshot.Items.Select(item => item.IsPass ? "P" : "F")),
            "1",
            context.Snapshot.IsPass == true ? "P" : "F",
            "0",
            context.StartTime.ToString("yyyyMMdd"),
            context.StartTime.ToString("HHmmss"),
            context.GetOption("SwVersion", context.Configuration.Identity.TestProgram),
            string.Empty,
            context.GetOption("TesterId", context.Configuration.Mes.Tooling),
            "0",
            string.Empty
        }));

        builder.AppendLine();
        builder.AppendLine("// Component_Data");
        builder.AppendLine("// StepNum, PartName, Act_V, Std_V, Offset, HLim, LLim, Mode, Type, HPin, LPin, Loc, Delay, Msr_V, Dev, Result");

        foreach (var item in context.Snapshot.Items)
        {
            builder.AppendLine(BuildDclComponentLine(item));
        }

        return builder.ToString();
    }

    private static string BuildDclComponentLine(TestItemResult item)
    {
        return Join(new[]
        {
            item.Index.ToString(),
            item.ItemName,
            "NA",
            "NA",
            "000",
            WithUnit(item.UpperLimit, item.Unit),
            WithUnit(item.LowerLimit, item.Unit),
            string.Empty,
            string.Empty,
            EmptyAsNa(item.PointA),
            EmptyAsNa(item.PointB),
            string.Empty,
            "0",
            WithUnit(item.TestValue, item.Unit),
            "NA",
            item.IsPass ? "0" : "1"
        });
    }

    private static ExportPluginConfigField Field(string key, string displayName, string defaultValue = "")
    {
        return new ExportPluginConfigField
        {
            Key = key,
            DisplayName = displayName,
            DefaultValue = defaultValue,
            IsRequired = false
        };
    }

    private static string EmptyAsNa(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "NA" : value;
    }

    private static string WithUnit(string value, string unit)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "NA", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(unit)
            || value.TrimEnd().EndsWith(unit, StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return value + unit;
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

    private static string MakeValidFileName(string value)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }
}
