using System.Text;

namespace CH_V03.Core.ResultExport;

public sealed class CsvMesResultExporter : IResultExporter, IResultExportPlugin
{
    public string Id => "Default.CsvMes";

    public string Name => "默认 CsvMes";

    public IReadOnlyList<ExportPluginConfigField> GetConfigFields()
    {
        return Array.Empty<ExportPluginConfigField>();
    }

    public async Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = context.Configuration.Output.DclDirectory;
            Directory.CreateDirectory(directory);
            var filePath = Path.Combine(directory, ResultExportFileName.ApplyTestPieceSuffix(context, $"{DateTime.Now:yyyyMMdd}.csv"));
            var fileExists = File.Exists(filePath);

            await using var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            if (!fileExists)
            {
                await writer.WriteLineAsync("SN,StartTime,Result,SwVersion");
            }

            await writer.WriteLineAsync(string.Join(',', new[]
            {
                Escape(context.Snapshot.Barcode),
                Escape(context.StartTime.ToString("yyyy/MM/dd HH:mm:ss")),
                Escape(context.ResultText),
                Escape(context.Configuration.Identity.TestProgram)
            }));

            return ResultExportResult.Success(Name, "CsvMes 生成成功", filePath);
        }
        catch (Exception ex)
        {
            return ResultExportResult.Fail(Name, $"CsvMes 生成失败：{ex.Message}");
        }
    }

    private static string Escape(string value)
    {
        value ??= string.Empty;
        return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}

