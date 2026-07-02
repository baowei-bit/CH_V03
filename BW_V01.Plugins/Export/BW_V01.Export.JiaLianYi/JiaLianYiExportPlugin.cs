using System.Text;
using BW_V01.Core.ResultExport;

namespace BW_V01.Export.JiaLianYi;

public sealed class JiaLianYiExportPlugin : IResultExportPlugin
{
    public string Id => "JiaLianYi.Export";

    public string Name => "嘉联益 CSV/DCL 导出";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields()
    {
        return new[]
        {
            Field("CsvDirectory", "CSV目录"),
            Field("DclDirectory", "DCL目录"),
            Field("CsvName", "CSV文件前缀"),
            Field("PartNumber", "产品料号"),
            Field("Station", "测试站别"),
            Field("ModifyUser", "测试人员"),
            Field("Mechine", "设备"),
            Field("Tooling", "治具"),
            Field("JobNo", "工单"),
            Field("OperationSeq", "制程序号"),
            Field("ItemCode", "品号"),
            Field("Unit", "单位", "pcs"),
            Field("Module", "模块", "1"),
            Field("SwVersion", "软件版本")
        };
    }

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var csvPath = await ExportCsvAsync(context, cancellationToken);
            var dclPath = string.Empty;
            if (!string.IsNullOrWhiteSpace(context.Snapshot.Barcode))
            {
                dclPath = await ExportDclAsync(context, cancellationToken);
            }

            var files = string.IsNullOrWhiteSpace(dclPath) ? csvPath : $"{csvPath}; {dclPath}";
            return ResultExportResult.Success(Name, "嘉联益导出完成", files);
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"嘉联益导出失败：{ex.Message}");
        }
    }

    private static async Task<string> ExportCsvAsync(ResultExportContext context, CancellationToken cancellationToken)
    {
        var directory = context.GetOption("CsvDirectory", context.Configuration.Output.CsvDirectory);
        Directory.CreateDirectory(directory);

        var csvName = context.GetOption("CsvName", context.Configuration.Output.CsvName);
        var filePath = Path.Combine(directory, $"{csvName}_{DateTime.Now:yyyyMMdd}.csv");
        var exists = File.Exists(filePath);

        await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);

        if (!exists)
        {
            await writer.WriteLineAsync(BuildHeader(context));
            await writer.WriteLineAsync(BuildHighPointLine(context));
            await writer.WriteLineAsync(BuildLowPointLine(context));
            await writer.WriteLineAsync(BuildLowerLimitLine(context));
            await writer.WriteLineAsync(BuildUpperLimitLine(context));
            await writer.WriteLineAsync(BuildUnitLine(context));
        }

        await writer.WriteLineAsync(BuildResultLine(context));
        return filePath;
    }

    private static async Task<string> ExportDclAsync(ResultExportContext context, CancellationToken cancellationToken)
    {
        var directory = context.GetOption("DclDirectory", context.Configuration.Output.DclDirectory);
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, MakeValidFileName(context.Snapshot.Barcode) + ".txt");
        await File.WriteAllTextAsync(filePath, BuildDcl(context), Encoding.Default, cancellationToken);
        return filePath;
    }

    private static string BuildHeader(ResultExportContext context)
    {
        var fixedHeaders = new[]
        {
            "SerialNumber", "Test Pass/Fail Status", "errCode", "PartNumber", "Station", "ModifyUser", "Mechine", "Tooling",
            "JobNo", "OperationSeq", "ltemCode", "Unit", "Module", "StartTime", "EndTime", "TestTime", "Failing items",
            "errValue", "swversion", "attribute1"
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

    private static string BuildHighPointLine(ResultExportContext context)
    {
        return "high_point----->," + Blank(19) + Join(context.Snapshot.Items.Select(item => item.PointA));
    }

    private static string BuildLowPointLine(ResultExportContext context)
    {
        return "low_point----->," + Blank(19) + Join(context.Snapshot.Items.Select(item => item.PointB));
    }

    private static string BuildResultLine(ResultExportContext context)
    {
        var values = new List<string>
        {
            context.Snapshot.Barcode,
            context.ResultText,
            context.FirstFailItem,
            context.GetOption("PartNumber"),
            context.GetOption("Station", context.Configuration.Identity.StationNo),
            context.GetOption("ModifyUser", context.Configuration.Mes.UserNo),
            context.GetOption("Mechine", context.Configuration.Identity.MachineNo),
            context.GetOption("Tooling", context.Configuration.Mes.Tooling),
            context.GetOption("JobNo", context.Configuration.Mes.ProdNo),
            context.GetOption("OperationSeq"),
            context.GetOption("ItemCode"),
            context.GetOption("Unit", "pcs"),
            context.GetOption("Module", "1"),
            context.StartTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.EndTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.TestSeconds.ToString("F2"),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.ItemName)),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.TestValue)),
            context.GetOption("SwVersion", context.Configuration.Identity.TestProgram),
            context.Guid
        };

        values.AddRange(context.Snapshot.Items.Select(item => item.TestValue));
        return Join(values);
    }

    private static string BuildDcl(ResultExportContext context)
    {
        var builder = new StringBuilder();
        builder.Append($"{DateTime.Now:yyyy/MM/dd HH:mm:ss},");
        builder.Append(",,,,");
        builder.Append($"{context.Snapshot.Barcode},");
        builder.Append($"Product part number:{context.GetOption("PartNumber")},");
        builder.Append($"JobNo:{context.GetOption("JobNo", context.Configuration.Mes.ProdNo)},");
        builder.Append($"Machine:{context.GetOption("Mechine", context.Configuration.Identity.MachineNo)},");
        builder.Append($"Fixture:{context.GetOption("Tooling", context.Configuration.Mes.Tooling)},");
        builder.Append($"OPID:{context.GetOption("ModifyUser", context.Configuration.Mes.UserNo)},");
        builder.Append($"stationNo:{context.GetOption("Station", context.Configuration.Identity.StationNo)},");

        foreach (var item in context.Snapshot.Items)
        {
            var point = string.IsNullOrWhiteSpace(item.PointA) && string.IsNullOrWhiteSpace(item.PointB)
                ? string.Empty
                : $"({item.PointA}-{item.PointB})";
            var spec = string.IsNullOrWhiteSpace(item.LowerLimit) && string.IsNullOrWhiteSpace(item.UpperLimit)
                ? string.Empty
                : $"[{item.LowerLimit},{item.UpperLimit}]";
            builder.Append($" {item.ItemName}{point}{spec}:{item.TestValue}{item.Unit}({(item.IsPass ? "OK" : "NG")});");
        }

        builder.Append($" Test Result: {context.ResultTextNgOk}.");
        return builder.ToString();
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

