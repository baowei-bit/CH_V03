using System.Text;
using CH_V03.Core.ResultExport;
using CH_V03.Core.TestFlow;

namespace CH_V03.Export.Bomin;

public sealed class BominExportPlugin : IResultExportPlugin
{
    public string Id => "Bomin.Export";

    public string Name => "博敏 CSV/DCL 导出";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields()
    {
        return new[]
        {
            Field("CsvPassDirectory", "PASS CSV目录", "D:\\VisionResult\\FPC\\ProData\\"),
            Field("CsvFailDirectory", "FAIL CSV目录", "D:\\VisionResult\\FPC\\ProYield\\"),
            Field("DclDirectory", "DCL目录", "D:\\VisionResult\\FPC\\ProOut\\"),
            Field("CsvMesDirectory", "CsvMes目录", "D:\\VisionResult\\FPC\\ProOut\\"),
            Field("CsvName", "CSV文件前缀", "F3409-0021-0021CS"),
            Field("UseBarcodeSubfolders", "按条码第9/8位分目录", "true"),
            Field("TesterId", "TesterID/治具编号", "FS00035535-0001"),
            Field("Config", "Config字段", "111"),
            Field("Holder", "Holder", "A"),
            Field("Slot", "Slot", "1"),
            Field("TestType", "测试类型", "FCT"),
            Field("SwVersion", "软件版本", "FCT-V1.0")
        };
    }

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var csvPath = await ExportCsvAsync(context, cancellationToken);
            var dclPath = await ExportDclAsync(context, cancellationToken);
            var csvMesPath = await ExportCsvMesAsync(context, cancellationToken);
            return ResultExportResult.Success(Name, "博敏 CSV/DCL 导出完成", $"{csvPath}; {dclPath}; {csvMesPath}");
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"博敏 CSV/DCL 导出失败：{ex.Message}");
        }
    }

    private static async Task<string> ExportCsvAsync(ResultExportContext context, CancellationToken cancellationToken)
    {
        var directory = GetResultDirectory(context, context.Snapshot.IsPass == true ? "CsvPassDirectory" : "CsvFailDirectory");
        Directory.CreateDirectory(directory);

        var csvName = context.GetOption("CsvName", context.Configuration.Output.CsvName);
        var filePath = Path.Combine(directory, ResultExportFileName.ApplyTestPieceSuffix(context, $"{Safe(csvName)}_{DateTime.Now:yyyyMMdd}.csv"));
        var exists = File.Exists(filePath);

        await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        if (!exists)
        {
            await writer.WriteLineAsync(BuildCsvHeader(context).AsMemory(), cancellationToken);
            await writer.WriteLineAsync(BuildLimitLine("Lower Limit----->", context.Snapshot.Items.Select(item => item.LowerLimit)).AsMemory(), cancellationToken);
            await writer.WriteLineAsync(BuildLimitLine("Upper Limit----->", context.Snapshot.Items.Select(item => item.UpperLimit)).AsMemory(), cancellationToken);
            await writer.WriteLineAsync(BuildLimitLine("Measurement Unit----->", context.Snapshot.Items.Select(item => item.Unit)).AsMemory(), cancellationToken);
        }

        await writer.WriteLineAsync(BuildCsvResultLine(context).AsMemory(), cancellationToken);
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

        var filePath = Path.Combine(directory, ResultExportFileName.ApplyTestPieceSuffix(context, Safe(context.Snapshot.Barcode) + ".dcl"));
        await File.WriteAllTextAsync(filePath, BuildDcl(context), Encoding.Default, cancellationToken);
        return filePath;
    }

    private static async Task<string> ExportCsvMesAsync(ResultExportContext context, CancellationToken cancellationToken)
    {
        var directory = context.GetOption("CsvMesDirectory", context.Configuration.Output.DclDirectory);
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, ResultExportFileName.ApplyTestPieceSuffix(context, $"{DateTime.Now:yyyyMMdd}.csv"));
        var exists = File.Exists(filePath);
        await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        if (!exists)
        {
            await writer.WriteLineAsync("SerialNumber,StartTime,Test Pass/Fail Status,型号版本".AsMemory(), cancellationToken);
        }

        var line = Join(new[]
        {
            context.Snapshot.Barcode,
            context.StartTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.ResultText,
            context.GetOption("SwVersion", context.Configuration.Identity.TestProgram)
        });
        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        return filePath;
    }

    private static string GetResultDirectory(ResultExportContext context, string optionKey)
    {
        var fallback = optionKey == "CsvPassDirectory"
            ? context.Configuration.Output.CsvDirectory
            : context.Configuration.Output.SinglePieceCsvDirectory;
        var directory = context.GetOption(optionKey, fallback);

        if (!IsTrue(context.GetOption("UseBarcodeSubfolders", "true")))
        {
            return directory;
        }

        var barcode = context.Snapshot.Barcode;
        if (barcode.Length < 9)
        {
            return directory;
        }

        return Path.Combine(directory, barcode[8].ToString(), barcode[7].ToString(), context.GetOption("CsvLeafDirectory", "CSV"));
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
            context.GetOption("TesterId", context.Configuration.User.FixtureNo),
            context.GetOption("Config", "111"),
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
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.ItemName)),
            "1",
            context.Snapshot.IsPass == true ? "P" : "F",
            "0",
            context.StartTime.ToString("yyyyMMdd"),
            context.StartTime.ToString("HHmmss"),
            context.GetOption("SwVersion", context.Configuration.Identity.TestProgram),
            string.Empty,
            context.GetOption("TesterId", context.Configuration.User.FixtureNo),
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
            item.TestValue,
            string.Empty,
            "0",
            item.UpperLimit,
            item.LowerLimit,
            string.Empty,
            item.Unit,
            EmptyAsNa(item.PointA),
            EmptyAsNa(item.PointB),
            string.Empty,
            "NA",
            item.TestValue,
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
            DefaultValue = defaultValue
        };
    }

    private static bool IsTrue(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static string EmptyAsNa(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "NA" : value;
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
