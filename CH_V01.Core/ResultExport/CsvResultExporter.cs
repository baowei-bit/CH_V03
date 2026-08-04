using System.Text;

namespace CH_V01.Core.ResultExport;

public sealed class CsvResultExporter : IResultExporter, IResultExportPlugin
{
    public string Id => "Default.Csv";

    public string Name => "默认 CSV";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields()
    {
        return Array.Empty<ExportPluginConfigField>();
    }

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = CsvOutputDirectoryResolver.Resolve(context.Configuration, context.Snapshot.IsPass == true);
            Directory.CreateDirectory(directory);

            var fileName = ResultExportFileName.ApplyTestPieceSuffix(context, $"{context.Configuration.Output.CsvName}_{DateTime.Now:yyyyMMdd}.csv");
            var filePath = Path.Combine(directory, fileName);
            var fileExists = File.Exists(filePath);

            await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);

            if (!fileExists)
            {
                await writer.WriteLineAsync(BuildHeader(context));
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
            "SerialNumber", "Test Pass/Fail Status", "errCode", "userNo", "stationNo", "retest", "packNo", "Tooling",
            "prodNo", "password", "ItemCode", "weight", "Module", "StartTime", "EndTime", "TestTime", "Failing items",
            "errValue", "swversion", "attribute1"
        };

        var itemHeaders = context.Snapshot.Items.Select(item => Escape(item.ItemName));
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
            config.Mes.UserNo,
            config.Identity.StationNo,
            string.Empty,
            config.Mes.PackNo,
            config.Mes.Tooling,
            config.Mes.ProdNo,
            config.Mes.Password,
            string.Empty,
            string.Empty,
            "1",
            context.StartTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.EndTime.ToString("yyyy/MM/dd HH:mm:ss"),
            context.TestSeconds.ToString("F2"),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.ItemName)),
            string.Join(';', context.Snapshot.Items.Where(item => !item.IsPass).Select(item => item.TestValue)),
            config.Identity.TestProgram,
            context.Guid
        };

        values.AddRange(context.Snapshot.Items.Select(item => item.TestValue));
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

