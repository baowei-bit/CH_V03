using System.Text;
using CH_V01.Core.TestFlow;

namespace CH_V01.Core.ResultExport;

/// <summary>Exports one test as a wide CSV with selectable summary fields and test-item rows.</summary>
public sealed class CustomCsvResultExporter : IResultExporter, IResultExportPlugin
{
    public string Id => "Custom.Csv";
    public string Name => "自定义 CSV 导出器";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields() =>
    [
        TextField("FileNameTemplate", "文件名模板", "{ProductName}_{TestType}_{SN}_{Result}_{Time}",
            "可用占位符：{ProductName}、{TestType}、{SN}、{Result}、{Time}；例如：{ProductName}_{Result}_{Time}"),
        CheckField("HeaderSN", "表头：SN 码"),
        CheckField("HeaderResult", "表头：测试结果"),
        CheckField("HeaderFailedItems", "表头：不良项"),
        CheckField("HeaderTestTime", "表头：测试时间"),
        CheckField("HeaderStation", "表头：站位"),
        CheckField("BodyIndex", "表体：序号"),
        CheckField("BodyItemName", "表体：测试项目"),
        CheckField("BodyPointA", "表体：点位 A"),
        CheckField("BodyPointB", "表体：点位 B"),
        CheckField("BodyLowerLimit", "表体：下限"),
        CheckField("BodyUpperLimit", "表体：上限"),
        CheckField("BodyUnit", "表体：单位"),
        CheckField("BodyTestValue", "表体：测试值"),
        CheckField("BodyResult", "表体：测试结果")
    ];

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = CsvOutputDirectoryResolver.Resolve(context.Configuration, context.Snapshot.IsPass == true);
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, BuildFileName(context));
            await File.WriteAllTextAsync(filePath, BuildContent(context), new UTF8Encoding(true), cancellationToken);
            return ResultExportResult.Success(Name, "自定义 CSV 生成成功", filePath);
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"自定义 CSV 生成失败：{ex.Message}");
        }
    }

    private static ExportPluginConfigField TextField(string key, string displayName, string defaultValue, string helpText) => new()
    {
        Key = key, DisplayName = displayName, DefaultValue = defaultValue, HelpText = helpText
    };

    private static ExportPluginConfigField CheckField(string key, string displayName) => new()
    {
        Key = key, DisplayName = displayName, DefaultValue = "true", IsCheckbox = true
    };

    private static string BuildFileName(ResultExportContext context)
    {
        var name = context.GetOption("FileNameTemplate", "{ProductName}_{TestType}_{SN}_{Result}_{Time}");
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{ProductName}"] = context.Configuration.Identity.ProductName,
            ["{TestType}"] = context.Configuration.Identity.TestType,
            ["{SN}"] = context.Snapshot.Barcode,
            ["{Result}"] = context.ResultText,
            ["{Time}"] = context.EndTime.ToString("yyyyMMdd_HHmmss")
        };
        foreach (var (token, value) in replacements)
        {
            name = name.Replace(token, value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim(' ', '.');
        name = string.IsNullOrWhiteSpace(name) ? "测试结果" : name;
        var fileName = name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.csv";
        return ResultExportFileName.ApplyTestPieceSuffix(context, fileName);
    }

    private static string BuildContent(ResultExportContext context)
    {
        var lines = new List<string>();
        var summary = new (string Label, string Value, string Key)[]
        {
            ("SN 码", context.Snapshot.Barcode, "HeaderSN"),
            ("测试结果", context.ResultText, "HeaderResult"),
            ("不良项", string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.ItemName)), "HeaderFailedItems"),
            ("测试时间", context.EndTime.ToString("yyyy/MM/dd HH:mm:ss"), "HeaderTestTime"),
            ("站位", context.Configuration.Identity.StationNo, "HeaderStation")
        };
        var enabledSummary = summary.Where(value => IsEnabled(context, value.Key)).ToList();
        var items = context.Snapshot.Items;
        if (enabledSummary.Count == 0 && items.Count == 0)
        {
            return string.Empty;
        }

        // 与客户样例 CSV 一致：摘要字段在左，每个测试项目横向占用一列。
        lines.Add(Join(enabledSummary.Select(value => value.Label).Concat(items.Select(item => item.ItemName))));
        AddItemRow(lines, enabledSummary.Count, items, "序号----->", "BodyIndex", item => item.Index.ToString(), context);
        AddItemRow(lines, enabledSummary.Count, items, "high_point----->", "BodyPointA", item => item.PointA, context);
        AddItemRow(lines, enabledSummary.Count, items, "low_point----->", "BodyPointB", item => item.PointB, context);
        AddItemRow(lines, enabledSummary.Count, items, "Lower Limit----->", "BodyLowerLimit", item => item.LowerLimit, context);
        AddItemRow(lines, enabledSummary.Count, items, "Upper Limit----->", "BodyUpperLimit", item => item.UpperLimit, context);
        AddItemRow(lines, enabledSummary.Count, items, "Measurement Unit----->", "BodyUnit", item => item.Unit, context);
        if (IsEnabled(context, "BodyTestValue"))
        {
            lines.Add(Join(enabledSummary.Select(value => value.Value).Concat(items.Select(item => item.TestValue))));
        }

        AddItemRow(lines, enabledSummary.Count, items, "测试结果----->", "BodyResult", item => item.Result, context);
        return string.Join(Environment.NewLine, lines);
    }

    private static void AddItemRow(
        ICollection<string> lines,
        int summaryColumnCount,
        IReadOnlyList<TestItemResult> items,
        string rowLabel,
        string optionKey,
        Func<TestItemResult, string> getValue,
        ResultExportContext context)
    {
        if (!IsEnabled(context, optionKey))
        {
            return;
        }

        var summaryValues = summaryColumnCount == 0
            ? Enumerable.Empty<string>()
            : new[] { rowLabel }.Concat(Enumerable.Repeat(string.Empty, summaryColumnCount - 1));
        lines.Add(Join(summaryValues.Concat(items.Select(getValue))));
    }

    private static bool IsEnabled(ResultExportContext context, string key) =>
        !context.PluginOptions.TryGetValue(key, out var value) || !bool.TryParse(value, out var enabled) || enabled;

    private static string Join(params string[] values) => Join((IEnumerable<string>)values);
    private static string Join(IEnumerable<string> values) => string.Join(',', values.Select(Escape));

    private static string Escape(string? value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
