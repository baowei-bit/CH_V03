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
            Field("ExportHorizontalCsv", "导出横版 CSV", "false", isCheckbox: true),
            Field("HorizontalCsvDirectory", "横版 CSV 保存路径", @"D:\YiDongEap\Result\Horizontal", true),
            Field("HorizontalCsvName", "横版 CSV 文件前缀", "YiDongEap"),
            Field("ExportVerticalCsv", "导出竖版 CSV", "true", isCheckbox: true),
            Field("CsvDirectory", "竖版 CSV 保存路径", @"D:\YiDongEap\Result", true),
            Field("HytmTxtDirectory", "HYTM TXT 保存路径（留空时使用竖版 CSV 路径）"),
            Field("ProgramId", "程序编号 ProgramId"),
            Field("PartNumber", "横版产品料号"),
            Field("Station", "横版测试站别"),
            Field("DeviceId", "设备编号 DeviceId", "", true),
            Field("FixtureId", "治具编号 FixtureId"),
            Field("LineId", "线体编号 LineId"),
            Field("BoardId", "板号 BoardID/BX", "B1"),
            Field("SectionName", "CSV 测试段名称", "CompTest"),
            Field("OpenTestIndexes", "OpenTest 测试序号（逗号分隔）"),
            Field("ShortTestIndexes", "ShortTest 测试序号（逗号分隔）"),
            Field("CompTestIndexes", "CompTest 测试序号（逗号分隔）"),
            Field("FunTestIndexes", "FunTest 测试序号（逗号分隔）"),
            Field("FunTestContent", "FunTest 明细（按行填写，空值不输出）"),
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
            var format = context.GetOption("OutputFormat", CsvFormat).Trim().ToUpperInvariant();
            return format switch
            {
                CsvFormat => await ExportSelectedCsvAsync(context, cancellationToken),
                HytmTxtFormat or "TXT" => await ExportHytmTxtAsync(context, GetHytmTxtDirectory(context), cancellationToken),
                BothFormats => await ExportAllSelectedFormatsAsync(context, cancellationToken),
                _ => ResultExportResult.Fail(Name, "输出格式无效，请填写 CSV、HYTM_TXT 或 BOTH")
            };
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"奕东测试结果导出失败：{ex.Message}");
        }
    }

    private async Task<ResultExportResult> ExportAllSelectedFormatsAsync(ResultExportContext context, CancellationToken cancellationToken)
    {
        var csvResult = await ExportSelectedCsvAsync(context, cancellationToken);
        if (!csvResult.IsSuccess)
        {
            return csvResult;
        }

        var txtResult = await ExportHytmTxtAsync(context, GetHytmTxtDirectory(context), cancellationToken);
        return txtResult.IsSuccess
            ? ResultExportResult.Success(Name, "奕东 CSV 和 HYTM TXT 已归档", CombinePaths(csvResult.FilePath, txtResult.FilePath))
            : ResultExportResult.Fail(Name, $"CSV 已归档：{csvResult.FilePath}；HYTM TXT 导出失败：{txtResult.Message}");
    }

    private async Task<ResultExportResult> ExportSelectedCsvAsync(ResultExportContext context, CancellationToken cancellationToken)
    {
        var exportHorizontal = IsEnabled(Value(context, "ExportHorizontalCsv", "false"));
        var exportVertical = IsEnabled(Value(context, "ExportVerticalCsv", "true"));
        if (!exportHorizontal && !exportVertical)
        {
            return ResultExportResult.Fail(Name, "请至少勾选一种 CSV 导出格式");
        }

        var paths = new List<string>();
        if (exportHorizontal)
        {
            paths.Add(await ExportHorizontalCsvAsync(context, GetHorizontalCsvDirectory(context), cancellationToken));
        }

        if (exportVertical)
        {
            paths.Add(await ExportVerticalCsvAsync(context, GetVerticalCsvDirectory(context), cancellationToken));
        }

        return ResultExportResult.Success(Name, $"奕东 CSV 已归档（{string.Join("、", new[] { exportHorizontal ? "横版" : null, exportVertical ? "竖版" : null }.Where(value => value is not null))}）", string.Join("; ", paths));
    }

    private async Task<string> ExportVerticalCsvAsync(ResultExportContext context, string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var now = context.EndTime;
        var failures = GetFailures(context).ToList();
        var typeAssignments = GetTypeAssignments(context);
        var suffix = Safe(context.GetOption("FileSuffix", "RIGHT"));
        // 竖版 CSV 按 PCS 独立归档，避免同一天的多片测试追加到同一个文件。
        var fileName = ResultExportFileName.ApplyTestPieceSuffix(
            context,
            $"{Safe(context.Snapshot.Barcode)}_{now:yyyyMMddHHmmssfff}_{suffix}.csv");
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
        await WriteKeyValueAsync(writer, "TestStat", GetTestStatus(failures, typeAssignments), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "OpenTest", GetSectionResult(failures, FailureKind.Open, typeAssignments), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "ShortTest", GetSectionResult(failures, FailureKind.Short, typeAssignments), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "CompTest", GetSectionResult(failures, FailureKind.Comp, typeAssignments), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "FunTest", GetSectionResult(failures, FailureKind.Fun, typeAssignments), cancellationToken);
        await writer.WriteLineAsync();
        await WriteKeyValueAsync(writer, "TestTime", $"{context.TestSeconds:F3} s", cancellationToken);
        await writer.WriteLineAsync();
        await WriteFailureDetailsAsync(writer, failures, typeAssignments, cancellationToken);

        await WriteTestDetailSectionAsync(writer, context, "CompTest", typeAssignments.Comp, cancellationToken);
        await WriteTestDetailSectionAsync(writer, context, "FunTest", typeAssignments.Fun, cancellationToken);
        await WriteFunTestSectionAsync(writer, context, cancellationToken);
        await WriteCsvAsync(writer, cancellationToken, "LogEnd");
        return path;
    }

    private async Task<string> ExportHorizontalCsvAsync(ResultExportContext context, string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var name = Safe(Value(context, "HorizontalCsvName", "YiDongEap"));
        var path = Path.Combine(directory, ResultExportFileName.ApplyTestPieceSuffix(context, $"{name}_{context.EndTime:yyyyMMdd}.csv"));
        var exists = File.Exists(path);

        await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = CreateWriter(stream);
        if (!exists)
        {
            await writer.WriteLineAsync(BuildHorizontalHeader(context));
            await writer.WriteLineAsync(BuildHorizontalPointLine(context, "high_point----->", item => item.PointA));
            await writer.WriteLineAsync(BuildHorizontalPointLine(context, "low_point----->", item => item.PointB));
            await writer.WriteLineAsync(BuildHorizontalPointLine(context, "Lower Limit----->", item => item.LowerLimit));
            await writer.WriteLineAsync(BuildHorizontalPointLine(context, "Upper Limit----->", item => item.UpperLimit));
            await writer.WriteLineAsync(BuildHorizontalPointLine(context, "Measurement Unit----->", item => item.Unit));
        }

        await writer.WriteLineAsync(BuildHorizontalResultLine(context));
        return path;
    }

    private async Task<ResultExportResult> ExportHytmTxtAsync(ResultExportContext context, string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
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

    private static string BuildHorizontalHeader(ResultExportContext context)
    {
        var fixedHeaders = new[]
        {
            "SerialNumber", "Test Pass/Fail Status", "errCode", "PartNumber", "Station", "ModifyUser", "Mechine", "Tooling",
            "JobNo", "OperationSeq", "ltemCode", "Unit", "Module", "StartTime", "EndTime", "TestTime", "Failing items",
            "errValue", "swversion", "attribute1"
        };

        return JoinRaw(fixedHeaders.Concat(context.Snapshot.Items.OrderBy(item => item.Index).Select(item => item.ItemName)));
    }

    private static string BuildHorizontalPointLine(ResultExportContext context, string label, Func<TestItemResult, string> valueSelector)
    {
        return label + "," + Blank(19) + JoinRaw(context.Snapshot.Items.OrderBy(item => item.Index).Select(valueSelector));
    }

    private static string BuildHorizontalResultLine(ResultExportContext context)
    {
        var values = new List<string>
        {
            context.Snapshot.Barcode,
            context.ResultText,
            context.FirstFailItem,
            Value(context, "PartNumber", context.Configuration.Identity.ProductName),
            Value(context, "Station", context.Configuration.Identity.StationNo),
            Value(context, "Operator", context.Configuration.User.LoginJobNo),
            Value(context, "DeviceId", context.Configuration.Identity.MachineNo),
            Value(context, "FixtureId", context.Configuration.User.FixtureNo),
            context.Configuration.Mes.ProdNo,
            string.Empty,
            string.Empty,
            "pcs",
            "1",
            context.StartTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.EndTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.TestSeconds.ToString("F2", CultureInfo.InvariantCulture),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.ItemName)),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.TestValue)),
            Value(context, "ProgramId", context.Configuration.Identity.TestProgram),
            context.Guid
        };

        values.AddRange(context.Snapshot.Items.OrderBy(item => item.Index).Select(item => item.TestValue));
        return JoinRaw(values);
    }

    private static string GetHorizontalCsvDirectory(ResultExportContext context)
    {
        return Value(context, "HorizontalCsvDirectory", Path.Combine(GetVerticalCsvDirectory(context), "Horizontal"));
    }

    private static string GetVerticalCsvDirectory(ResultExportContext context)
    {
        return Value(context, "CsvDirectory", context.Configuration.Output.CsvDirectory);
    }

    private static string GetHytmTxtDirectory(ResultExportContext context)
    {
        var directory = Value(context, "HytmTxtDirectory").Trim();
        return string.IsNullOrWhiteSpace(directory) ? GetVerticalCsvDirectory(context) : directory;
    }

    private static string CombinePaths(params string[] paths) => string.Join("; ", paths.Where(path => !string.IsNullOrWhiteSpace(path)));

    private static StreamWriter CreateWriter(FileStream stream)
    {
        // Excel commonly opens CSV by the local ANSI code page unless a BOM is present.
        // Emit the UTF-8 BOM so Chinese labels and full-width separators stay readable.
        return new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true))
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

    private static IEnumerable<TestItemResult> GetFailures(ResultExportContext context) =>
        context.Snapshot.Items.Where(item => !item.IsPass);

    private static string GetTestStatus(IReadOnlyCollection<TestItemResult> failures, TestTypeAssignments typeAssignments)
    {
        if (failures.Count == 0)
        {
            return "Pass";
        }

        var firstFailureKind = failures
            .Select(item => GetFailureKind(item, typeAssignments))
            .FirstOrDefault(kind => kind.HasValue);

        return firstFailureKind switch
        {
            FailureKind.Open => "OpenFail",
            FailureKind.Short => "ShortFail",
            FailureKind.Comp => "CompFail",
            FailureKind.Fun => "FunFail",
            _ => "Pass"
        };
    }

    private static string GetSectionResult(IEnumerable<TestItemResult> failures, FailureKind kind, TestTypeAssignments typeAssignments) =>
        failures.Any(item => GetFailureKind(item, typeAssignments) == kind) ? "Fail" : "Pass";

    private static async Task WriteFailureDetailsAsync(StreamWriter writer, IReadOnlyCollection<TestItemResult> failures, TestTypeAssignments typeAssignments, CancellationToken cancellationToken)
    {
        foreach (var kind in new[] { FailureKind.Open, FailureKind.Short })
        {
            var items = failures.Where(item => GetFailureKind(item, typeAssignments) == kind).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            var name = kind == FailureKind.Open ? "OpenFail" : "ShortFail";
            var pointPairs = items.SelectMany(BuildPointPairs).ToList();
            await WriteKeyValueAsync(writer, name + "Count", pointPairs.Count.ToString(CultureInfo.InvariantCulture), cancellationToken);
            await writer.WriteLineAsync();
            foreach (var pointPair in pointPairs)
            {
                await WriteCsvAsync(writer, cancellationToken, ToExcelText(pointPair));
            }

            await writer.WriteLineAsync();
        }
    }

    private static async Task WriteTestDetailSectionAsync(
        StreamWriter writer,
        ResultExportContext context,
        string sectionName,
        ISet<int> indexes,
        CancellationToken cancellationToken)
    {
        // Open and Short only output their failure point pairs above. Detailed tables
        // are reserved for the configured CompTest and FunTest item sequences.
        if (indexes.Count == 0)
        {
            return;
        }

        var items = context.Snapshot.Items
            .Where(item => indexes.Contains(item.Index))
            .OrderBy(item => item.Index)
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        await WriteCsvAsync(writer, cancellationToken, sectionName + ":");
        var isFunTest = string.Equals(sectionName, "FunTest", StringComparison.Ordinal);
        await WriteCsvAsync(
            writer,
            cancellationToken,
            isFunTest
                ? new[] { "Step", "BX", "Descrip", "Module", "Type", "Function", "Delay", "Test", "Dev%", "-%", "+%", "Result", "Info" }
                : new[] { "STEP", "BX", "LC", "Flag", "Device Name", "STDval", "ACTval", "+%", "-%", "MD", "RG", "TM", "A", "B", "G-1", "G-2", "G-3", "G-4", "G-5", "TestVal", "Dev%", "Result" });
        foreach (var item in items)
        {
            foreach (var detail in ExpandItemDetails(item))
            {
                if (isFunTest)
                {
                    await WriteCsvAsync(writer, cancellationToken,
                        item.Index.ToString(CultureInfo.InvariantCulture),
                        Value(context, "BoardId", "B1"),
                        item.ItemName,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        detail.TestValue,
                        string.Empty,
                        item.LowerLimit,
                        item.UpperLimit,
                        item.Result,
                        BuildPointPair(detail));
                    continue;
                }

                await WriteCsvAsync(writer, cancellationToken,
                    item.Index.ToString(CultureInfo.InvariantCulture),
                    Value(context, "BoardId", "B1"),
                    detail.PointA,
                    string.Empty,
                    item.ItemName,
                    BuildStandard(item),
                    string.Empty,
                    item.UpperLimit,
                    item.LowerLimit,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    detail.PointA,
                    detail.PointB,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    detail.TestValue,
                    string.Empty,
                    item.Result);
            }
        }

        await writer.WriteLineAsync();
    }

    private static string BuildPointPair(TestItemResult item)
    {
        if (string.IsNullOrWhiteSpace(item.PointA) && string.IsNullOrWhiteSpace(item.PointB))
        {
            return string.Empty;
        }

        return $"{item.PointA} - {item.PointB}";
    }

    private static async Task WriteFunTestSectionAsync(StreamWriter writer, ResultExportContext context, CancellationToken cancellationToken)
    {
        var content = Value(context, "FunTestContent");
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        await WriteCsvAsync(writer, cancellationToken, "FunTest:");
        foreach (var line in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            await writer.WriteLineAsync(line);
        }

        await writer.WriteLineAsync();
    }

    private static IEnumerable<string> BuildPointPairs(TestItemResult item)
    {
        var pointAs = (item.PointA ?? string.Empty).Split('；', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var pointBs = (item.PointB ?? string.Empty).Split('；', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (pointAs.Length == 0 && pointBs.Length == 0)
        {
            return new[] { item.ItemName };
        }

        if (pointAs.Length == pointBs.Length)
        {
            return pointAs.Select((value, index) => $"{value} - {pointBs[index]}");
        }

        return new[] { $"{item.PointA} - {item.PointB}" };
    }

    private static IEnumerable<TestItemResult> ExpandItemDetails(TestItemResult item)
    {
        var pointAs = SplitDetails(item.PointA);
        var pointBs = SplitDetails(item.PointB);
        var values = SplitDetails(item.TestValue);
        var detailCount = new[] { pointAs.Length, pointBs.Length, values.Length }.Max();
        if (detailCount <= 1 || pointAs.Length != detailCount || pointBs.Length != detailCount || values.Length != detailCount)
        {
            return new[] { item };
        }

        return Enumerable.Range(0, detailCount).Select(index => new TestItemResult
        {
            Index = item.Index,
            ItemName = item.ItemName,
            PointA = pointAs[index],
            PointB = pointBs[index],
            LowerLimit = item.LowerLimit,
            UpperLimit = item.UpperLimit,
            Unit = item.Unit,
            TestValue = values[index],
            Result = item.Result
        });
    }

    private static string[] SplitDetails(string value) =>
        (value ?? string.Empty).Split('；', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static string ToExcelText(string value)
    {
        // CSV cells are interpreted by Excel. A formula that evaluates to text keeps
        // the displayed value unchanged without showing a leading apostrophe.
        return $"=\"{value.Replace("\"", "\"\"")}\"";
    }

    private static TestTypeAssignments GetTypeAssignments(ResultExportContext context)
    {
        return new TestTypeAssignments(
            ParseIndexes(Value(context, "OpenTestIndexes")),
            ParseIndexes(Value(context, "ShortTestIndexes")),
            ParseIndexes(Value(context, "CompTestIndexes")),
            ParseIndexes(Value(context, "FunTestIndexes")));
    }

    private static HashSet<int> ParseIndexes(string value)
    {
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : 0)
            .Where(index => index > 0)
            .ToHashSet();
    }

    private static FailureKind? GetFailureKind(TestItemResult item, TestTypeAssignments typeAssignments)
    {
        if (typeAssignments.Open.Contains(item.Index))
        {
            return FailureKind.Open;
        }

        if (typeAssignments.Short.Contains(item.Index))
        {
            return FailureKind.Short;
        }

        if (typeAssignments.Comp.Contains(item.Index))
        {
            return FailureKind.Comp;
        }

        if (typeAssignments.Fun.Contains(item.Index))
        {
            return FailureKind.Fun;
        }

        return null;
    }

    private enum FailureKind
    {
        Open,
        Short,
        Comp,
        Fun
    }

    private sealed record TestTypeAssignments(HashSet<int> Open, HashSet<int> Short, HashSet<int> Comp, HashSet<int> Fun);

    private static string Value(ResultExportContext context, string key, string defaultValue = "")
    {
        return context.GetOption(key, defaultValue);
    }

    private static ExportPluginConfigField Field(string key, string displayName, string defaultValue = "", bool required = false, bool isCheckbox = false)
    {
        return new ExportPluginConfigField
        {
            Key = key,
            DisplayName = displayName,
            DefaultValue = defaultValue,
            IsRequired = required,
            IsCheckbox = isCheckbox
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

    private static string Blank(int count) => string.Concat(Enumerable.Repeat(",", count));

    private static string JoinRaw(IEnumerable<string> values) => string.Join(',', values.Select(value => value ?? string.Empty));

    private static bool IsEnabled(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
