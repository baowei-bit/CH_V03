using System.Text;
using CH_V03.Core.TestFlow;

namespace CH_V03.Core.ResultExport;

public sealed class CsvResultExporter : IResultExporter, IResultExportPlugin
{
    public string Id => "Default.Csv";

    public string Name => "默认 CSV";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields()
    {
        return [new ExportPluginConfigField
        {
            Key = SpecialTestValue.OptionKey,
            DisplayName = "特殊项测试值隐藏",
            DefaultValue = "false",
            IsCheckbox = true
        }];
    }

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
        => await WriteAsync(context, context.Configuration.Output.CsvName, cancellationToken);

    public static string JingWangFileName(CH_V03.Core.Configuration.AppConfiguration config, DateTime day)
    {
        var fields = config.Mes.PluginOptions;
        var name = $"{fields.GetValueOrDefault("Product")}-{fields.GetValueOrDefault("SpecCode")}-{day:yyyyMMdd}.csv";
        return string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }

    internal async Task<ResultExportResult> WriteAsync(ResultExportContext context, string prefix, CancellationToken cancellationToken)
    {
        try
        {
            var directory = CsvOutputDirectoryResolver.Resolve(context.Configuration, context.Snapshot.IsPass == true);
            Directory.CreateDirectory(directory);

            var fileName = ResultExportFileName.ApplyTestPieceSuffix(context, $"{prefix}_{DateTime.Now:yyyyMMdd}.csv");
            if (context.IsJingWangAccount) fileName = JingWangFileName(context.Configuration, context.EndTime);
            var header = context.IsJingWangAccount ? SinglePieceCsvResultExporter.BuildHeader(context) : BuildHeader(context);
            var filePath = ResolveFilePath(directory, fileName, header);
            var fileExists = File.Exists(filePath);

            await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);

            if (context.IsJingWangAccount)
            {
                if (!fileExists)
                {
                    await writer.WriteLineAsync(header);
                    await writer.WriteLineAsync(SinglePieceCsvResultExporter.BuildLowerLimitLine(context));
                    await writer.WriteLineAsync(SinglePieceCsvResultExporter.BuildUpperLimitLine(context));
                    await writer.WriteLineAsync(SinglePieceCsvResultExporter.BuildUnitLine(context));
                }
                await writer.WriteLineAsync(SinglePieceCsvResultExporter.BuildResultLine(context));
                return ResultExportResult.Success(Name, "景旺汇总 CSV 生成成功", filePath);
            }
            if (!fileExists)
            {
                await writer.WriteLineAsync(header);
                await writer.WriteLineAsync(BuildItemMetadataLine(context, "high_point----->", item => item.PointA));
                await writer.WriteLineAsync(BuildItemMetadataLine(context, "low_point----->", item => item.PointB));
                await writer.WriteLineAsync(BuildItemMetadataLine(context, "Lower Limit----->", item => item.LowerLimit));
                await writer.WriteLineAsync(BuildItemMetadataLine(context, "Upper Limit----->", item => item.UpperLimit));
                await writer.WriteLineAsync(BuildItemMetadataLine(context, "Measurement Unit----->", item => item.Unit));
            }

            await writer.WriteLineAsync(BuildRow(context));
            return ResultExportResult.Success(Name, "CSV 生成成功", filePath);
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"CSV 生成失败：{ex.Message}");
        }
    }

    private static string BuildHeader(ResultExportContext context)
    {
        var fixedHeaders = new[]
        {
            "SerialNumber", "Test Pass/Fail Status", "errCode", "PartNumber", "Station", "ModifyUser", "Mechine", "Tooling",
            "JobNo", "OperationSeq", "ltemCode", "Unit", "Module", "StartTime", "EndTime", "TestTime", "Failing items",
            "errValue", "swversion", "attribute1"
        };

        var itemHeaders = context.Snapshot.Items.OrderBy(item => item.Index).Select(item => Escape(item.ItemName));
        return string.Join(',', fixedHeaders.Concat(itemHeaders));
    }

    private static string BuildRow(ResultExportContext context)
    {
        var config = context.Configuration;
        var values = new List<string>
        {
            context.Snapshot.Barcode,
            context.ResultText,
            context.FirstFailItem,
            context.GetOption("PartNumber", config.Identity.ProductName),
            context.GetOption("Station", config.Identity.StationNo),
            context.GetOption("ModifyUser", string.IsNullOrWhiteSpace(config.Mes.UserNo) ? config.User.LoginJobNo : config.Mes.UserNo),
            context.GetOption("Mechine", config.Identity.MachineNo),
            context.GetOption("Tooling", string.IsNullOrWhiteSpace(config.Mes.Tooling) ? config.User.FixtureNo : config.Mes.Tooling),
            context.GetOption("JobNo", config.Mes.ProdNo),
            context.GetOption("OperationSeq"),
            context.GetOption("ItemCode"),
            context.GetOption("Unit", "pcs"),
            context.GetOption("Module", "1"),
            context.StartTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.EndTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.TestSeconds.ToString("F2"),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.ItemName)),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => SpecialTestValue.CsvValue(context, item))),
            context.GetOption("SwVersion", config.Identity.TestProgram),
            context.Guid
        };

        values.AddRange(context.Snapshot.Items.OrderBy(item => item.Index).Select(item => SpecialTestValue.CsvValue(context, item)));
        return string.Join(',', values.Select(Escape));
    }

    private static string ResolveFilePath(string directory, string fileName, string header)
    {
        var filePath = Path.Combine(directory, fileName);
        if (!File.Exists(filePath) || File.ReadLines(filePath).FirstOrDefault() == header)
        {
            return filePath;
        }

        // 当天已有旧表头时，在同一天的独立文件中继续汇总，避免新旧列错位。
        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var suffix = 1; ; suffix++)
        {
            var candidate = Path.Combine(directory, $"{name}_v{suffix}{extension}");
            if (!File.Exists(candidate) || File.ReadLines(candidate).FirstOrDefault() == header)
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// 默认 CSV 的测试项元数据行：固定列保持为空，
    /// 点位、限值及单位与后续每个测试项列严格对齐。
    /// </summary>
    private static string BuildItemMetadataLine(
        ResultExportContext context,
        string label,
        Func<TestItemResult, string> valueSelector)
    {
        const int fixedColumnCount = 20;
        var values = new[] { label }
            .Concat(Enumerable.Repeat(string.Empty, fixedColumnCount - 1))
            .Concat(context.Snapshot.Items.OrderBy(item => item.Index).Select(valueSelector));

        return string.Join(',', values.Select(Escape));
    }

    private static string Escape(string value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}

