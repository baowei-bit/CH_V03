using System.Globalization;
using System.Text;
using CH_V01.Core.ResultExport;
using CH_V01.Core.TestFlow;

namespace CH_V01.Export.YiDongEap;

/// <summary>
/// 奕东测试结果导出器。
/// 支持原始 CSV 明细格式，以及 HYTM Statistic TXT 汇总格式。
/// </summary>
public sealed class YiDongEapRawCsvExportPlugin : IResultExportPlugin
{
    private const string CsvFormat = "CSV";
    private const string HytmTxtFormat = "HYTM_TXT";
    private const string BothFormats = "BOTH";

    public string Id => "YiDongEap.RawCsv";

    public string Name => "奕东 EAP 测试结果导出";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields()
    {
        return new[]
        {
            Field("OutputFormat", "输出格式 CSV、HYTM_TXT 或 BOTH", BothFormats, true),
            Field("CsvDirectory", "结果归档目录", @"D:\YiDongEap\Result", true),
            Field("ProgramId", "程序编号 ProgramId"),
            Field("DeviceId", "设备编号 DeviceId", "", true),
            Field("FixtureId", "治具编号 FixtureId"),
            Field("LineId", "线体编号 LineId"),
            Field("BoardId", "板号 BoardID/BX", "B1"),
            Field("SectionName", "CSV 测试段名称", "CompTest"),
            Field("FileSuffix", "CSV 文件名尾缀", "RIGHT"),
            Field("Operator", "操作员工号/姓名"),
            Field("HytmTitle", "HYTM TXT 标题", "HYTM1_1_1 Statistic"),
            Field("TestFilePath", "HYTM 测试文件路径 FilePath"),
            Field("BlockNum", "HYTM BlockNum", "1"),
            Field("BlockGroup", "HYTM BlockGroup", "0-0"),
            Field("OsErrorCode", "HYTM OSErr", "0"),
            Field("CircuitErrorCode", "HYTM CERR", "0"),
            Field("EquipmentNo", "HYTM EQUIPMENT_NO")
        };
    }

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = context.GetOption("CsvDirectory", context.Configuration.Output.CsvDirectory);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return ResultExportResult.Fail(Name, "结果归档目录不能为空");
            }

            var format = context.GetOption("OutputFormat", CsvFormat).Trim().ToUpperInvariant();
            Directory.CreateDirectory(directory);
            return format switch
            {
                CsvFormat => await ExportCsvAsync(context, directory, cancellationToken),
                HytmTxtFormat or "TXT" => await ExportHytmTxtAsync(context, directory, cancellationToken),
                BothFormats => await ExportBothAsync(context, directory, cancellationToken),
                _ => ResultExportResult.Fail(Name, "输出格式无效，请填写 CSV、HYTM_TXT 或 BOTH")
            };
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"奕东测试结果导出失败：{ex.Message}");
        }
    }

    private async Task<ResultExportResult> ExportBothAsync(ResultExportContext context, string directory, CancellationToken cancellationToken)
    {
        var csvResult = await ExportCsvAsync(context, directory, cancellationToken);
        if (!csvResult.IsSuccess)
        {
            return csvResult;
        }

        var txtResult = await ExportHytmTxtAsync(context, directory, cancellationToken);
        return txtResult.IsSuccess
            ? ResultExportResult.Success(Name, "奕东 CSV 和 HYTM TXT 已归档", $"{csvResult.FilePath}; {txtResult.FilePath}")
            : ResultExportResult.Fail(Name, $"CSV 已归档：{csvResult.FilePath}；HYTM TXT 导出失败：{txtResult.Message}");
    }

    private async Task<ResultExportResult> ExportCsvAsync(ResultExportContext context, string directory, CancellationToken cancellationToken)
    {
        var now = context.EndTime;
        var result = GetResult(context);
        var suffix = Safe(context.GetOption("FileSuffix", "RIGHT"));
        var fileName = ResultExportFileName.ApplyTestPieceSuffix(context, $"{Safe(context.Snapshot.Barcode)}_{now:yyyyMMddHHmmss}_{result}_{suffix}.csv");
        var path = Path.Combine(directory, fileName);

        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        await using var writer = CreateWriter(stream);

        await WriteKeyValueAsync(writer, "DateTime", now.ToString("yyyy-MM-dd HH:mm:ss"), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "ProgramId", Value(context, "ProgramId", context.Configuration.Identity.TestProgram), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "User", Value(context, "Operator", context.Configuration.User.LoginJobNo), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "DeviceId", Value(context, "DeviceId", context.Configuration.Identity.MachineNo), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "FixtureId", Value(context, "FixtureId", context.Configuration.User.FixtureNo), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "LineId", Value(context, "LineId", context.Configuration.Identity.StationNo), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "ModelName", context.Configuration.Identity.ProductName, cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "BoardId", Value(context, "BoardId", "B1"), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "BarCode", context.Snapshot.Barcode, cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "TestStat", result, cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "TestTime", $"{context.TestSeconds:F3} s", cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "FailItem", context.FirstFailItem, cancellationToken);
        await writer.WriteLineAsync();

        await WriteCsvAsync(writer, cancellationToken, Value(context, "SectionName", "CompTest") + ":");
        await WriteCsvAsync(writer, cancellationToken, "STEP", "BX", "LC", "Flag", "Device Name", "STDval", "ACTval", "+%", "-%", "MD", "RG", "TM", "A", "B", "G-1", "G-2", "G-3", "G-4", "G-5", "TestVal", "Dev%", "Result");

        foreach (var item in context.Snapshot.Items.OrderBy(item => item.Index))
        {
            await WriteCsvAsync(writer, cancellationToken,
                item.Index.ToString(CultureInfo.InvariantCulture),
                Value(context, "BoardId", "B1"),
                item.PointA,
                string.Empty,
                item.ItemName,
                BuildStandard(item),
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                item.PointA,
                item.PointB,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                item.TestValue,
                string.Empty,
                item.Result);
        }

        await writer.WriteLineAsync();
        await WriteCsvAsync(writer, cancellationToken, "LogEnd");
        return ResultExportResult.Success(Name, "奕东原始 CSV 已归档", path);
    }

    private async Task<ResultExportResult> ExportHytmTxtAsync(ResultExportContext context, string directory, CancellationToken cancellationToken)
    {
        var startTime = context.StartTime;
        var endTime = context.EndTime;
        var result = GetResult(context);
        var fileName = ResultExportFileName.ApplyTestPieceSuffix(context, $"{Safe(context.Snapshot.Barcode)}_{endTime:yyyyMMddHHmmss}_{result}.txt");
        var path = Path.Combine(directory, fileName);

        var lines = new[]
        {
            Value(context, "HytmTitle", "HYTM1_1_1 Statistic"),
            $"Time:{endTime:yyyyMMdd HH:mm:ss} {startTime:yyyyMMddHHmmss}",
            $"OperatorName:{Value(context, "Operator", context.Configuration.User.LoginJobNo)}",
            $"FilePath:{Value(context, "TestFilePath")}",
            $"BoardID:{Value(context, "BoardId", context.Snapshot.Barcode)}",
            $"BlockNum:{Value(context, "BlockNum", "1")}",
            $"BlockGroup={Value(context, "BlockGroup", "0-0")}",
            $"Barcode={context.Snapshot.Barcode}",
            $"Group_Status={result}",
            $"OSErr={Value(context, "OsErrorCode", "0")}",
            $"CERR={Value(context, "CircuitErrorCode", "0")}",
            $"EQUIPMENT_NO:{Value(context, "EquipmentNo", Value(context, "DeviceId", context.Configuration.Identity.MachineNo))}"
        };

        await File.WriteAllTextAsync(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
        return ResultExportResult.Success(Name, "奕东 HYTM TXT 已归档", path);
    }

    private static StreamWriter CreateWriter(FileStream stream)
    {
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            NewLine = "\r\n"
        };
    }

    private static Task WriteKeyValueAsync(StreamWriter writer, string key, string value, CancellationToken cancellationToken)
    {
        return WriteCsvAsync(writer, cancellationToken, key, value, string.Empty);
    }

    private static async Task WriteCsvAsync(StreamWriter writer, CancellationToken cancellationToken, params string[] values)
    {
        var line = string.Join(',', values.Select(Quote));
        await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
    }

    private static string BuildStandard(TestItemResult item)
    {
        return string.IsNullOrWhiteSpace(item.LowerLimit) && string.IsNullOrWhiteSpace(item.UpperLimit)
            ? string.Empty
            : $"{item.LowerLimit}~{item.UpperLimit}";
    }

    private static string GetResult(ResultExportContext context)
    {
        return context.Snapshot.IsPass == true ? "PASS" : "FAIL";
    }

    private static string Value(ResultExportContext context, string key, string defaultValue = "")
    {
        return context.GetOption(key, defaultValue);
    }

    private static ExportPluginConfigField Field(string key, string displayName, string defaultValue = "", bool required = false)
    {
        return new ExportPluginConfigField
        {
            Key = key,
            DisplayName = displayName,
            DefaultValue = defaultValue,
            IsRequired = required
        };
    }

    private static string Quote(string value)
    {
        return $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";
    }

    private static string Safe(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "NA" : value.Trim();
        foreach (var character in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(character, '_');
        }

        return value;
    }
}
