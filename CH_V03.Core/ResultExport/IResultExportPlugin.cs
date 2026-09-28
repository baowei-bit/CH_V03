namespace CH_V03.Core.ResultExport;

public interface IResultExportPlugin
{
    string Id { get; }

    string Name { get; }

    IReadOnlyList<ExportPluginConfigField> GetConfigFields();

    Task<ResultExportResult> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default);
}
