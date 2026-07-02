namespace BW_V01.Core.ResultExport;

public interface IResultExporter
{
    string Name { get; }

    Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default);
}
