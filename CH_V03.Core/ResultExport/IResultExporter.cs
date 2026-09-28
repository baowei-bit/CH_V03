namespace CH_V03.Core.ResultExport;

public interface IResultExporter
{
    string Name { get; }

    Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default);
}
