using System.Text;
using CH_V01.Core.TestFlow;

namespace CH_V01.Core.ResultExport;

/// <summary>Exports one test with selectable summary fields and test-item columns.</summary>
public sealed class CustomCsvResultExporter : IResultExporter, IResultExportPlugin
{
    public string Id => "Custom.Csv";

    public string Name => "\u81ea\u5b9a\u4e49 CSV \u5bfc\u51fa\u5668";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields() =>
    [
        TextField("FileNameTemplate", "\u6587\u4ef6\u540d\u6a21\u677f", "{ProductName}_{TestType}_{SN}_{Result}_{Time}",
            "\u53ef\u7528\u5360\u4f4d\u7b26\uff1a{ProductName}\u3001{TestType}\u3001{SN}\u3001{Result}\u3001{Time}\uff1b\u4f8b\u5982\uff1a{ProductName}_{Result}_{Time}"),
        CheckField("HeaderSN", "\u8868\u5934\uff1aSN \u7801"),
        CheckField("HeaderResult", "\u8868\u5934\uff1a\u6d4b\u8bd5\u7ed3\u679c"),
        CheckField("HeaderFailedItems", "\u8868\u5934\uff1a\u4e0d\u826f\u9879"),
        CheckField("HeaderTestTime", "\u8868\u5934\uff1a\u6d4b\u8bd5\u65f6\u95f4"),
        CheckField("HeaderStation", "\u8868\u5934\uff1a\u7ad9\u4f4d"),
        CheckField("BodyIndex", "\u8868\u4f53\uff1a\u5e8f\u53f7"),
        CheckField("BodyItemName", "\u8868\u4f53\uff1a\u6d4b\u8bd5\u9879\u76ee"),
        CheckField("BodyPointA", "\u8868\u4f53\uff1a\u70b9\u4f4d A"),
        CheckField("BodyPointB", "\u8868\u4f53\uff1a\u70b9\u4f4d B"),
        CheckField("BodyLowerLimit", "\u8868\u4f53\uff1a\u4e0b\u9650"),
        CheckField("BodyUpperLimit", "\u8868\u4f53\uff1a\u4e0a\u9650"),
        CheckField("BodyUnit", "\u8868\u4f53\uff1a\u5355\u4f4d"),
        CheckField("BodyTestValue", "\u8868\u4f53\uff1a\u6d4b\u8bd5\u503c"),
        CheckField("BodyResult", "\u8868\u4f53\uff1a\u6d4b\u8bd5\u7ed3\u679c")
    ];

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = CsvOutputDirectoryResolver.Resolve(context.Configuration, context.Snapshot.IsPass == true);
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, BuildFileName(context));
            await File.WriteAllTextAsync(filePath, BuildContent(context), new UTF8Encoding(true), cancellationToken);
            return ResultExportResult.Success(Name, "\u81ea\u5b9a\u4e49 CSV \u751f\u6210\u6210\u529f", filePath);
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"\u81ea\u5b9a\u4e49 CSV \u751f\u6210\u5931\u8d25\uff1a{ex.Message}");
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
        name = string.IsNullOrWhiteSpace(name) ? "\u6d4b\u8bd5\u7ed3\u679c" : name;
        var fileName = name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.csv";
        return ResultExportFileName.ApplyTestPieceSuffix(context, fileName);
    }

    private static string BuildContent(ResultExportContext context)
    {
        var lines = new List<string>();
        var summary = new (string Label, string Value, string Key)[]
        {
            ("\u0053\u004e \u7801", context.Snapshot.Barcode, "HeaderSN"),
            ("\u6d4b\u8bd5\u7ed3\u679c", context.ResultText, "HeaderResult"),
            ("\u4e0d\u826f\u9879", string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.ItemName)), "HeaderFailedItems"),
            ("\u6d4b\u8bd5\u65f6\u95f4", context.EndTime.ToString("yyyy/MM/dd HH:mm:ss"), "HeaderTestTime"),
            ("\u7ad9\u4f4d", context.Configuration.Identity.StationNo, "HeaderStation")
        };
        var columns = new (string Label, string Key, Func<TestItemResult, string> Value)[]
        {
            ("\u5e8f\u53f7", "BodyIndex", item => item.Index.ToString()),
            ("\u6d4b\u8bd5\u9879\u76ee", "BodyItemName", item => item.ItemName),
            ("\u70b9\u4f4d A", "BodyPointA", item => item.PointA),
            ("\u70b9\u4f4d B", "BodyPointB", item => item.PointB),
            ("\u4e0b\u9650", "BodyLowerLimit", item => item.LowerLimit),
            ("\u4e0a\u9650", "BodyUpperLimit", item => item.UpperLimit),
            ("\u5355\u4f4d", "BodyUnit", item => item.Unit),
            ("\u6d4b\u8bd5\u503c", "BodyTestValue", item => item.TestValue),
            ("\u6d4b\u8bd5\u7ed3\u679c", "BodyResult", item => item.Result)
        }.Where(column => IsEnabled(context, column.Key)).ToList();

        var enabledSummary = summary.Where(value => IsEnabled(context, value.Key)).ToList();
        if (enabledSummary.Count > 0 || columns.Count > 0)
        {
            // 摘要字段在左，测试项目表体字段紧随其后，组成同一张横向表。
            lines.Add(Join(enabledSummary.Select(value => value.Label).Concat(columns.Select(column => column.Label))));

            var rowCount = Math.Max(1, context.Snapshot.Items.Count);
            for (var index = 0; index < rowCount; index++)
            {
                var summaryValues = index == 0
                    ? enabledSummary.Select(value => value.Value)
                    : Enumerable.Repeat(string.Empty, enabledSummary.Count);
                var bodyValues = index < context.Snapshot.Items.Count
                    ? columns.Select(column => column.Value(context.Snapshot.Items[index]))
                    : Enumerable.Repeat(string.Empty, columns.Count);
                lines.Add(Join(summaryValues.Concat(bodyValues)));
            }
        }

        return string.Join(Environment.NewLine, lines);
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
