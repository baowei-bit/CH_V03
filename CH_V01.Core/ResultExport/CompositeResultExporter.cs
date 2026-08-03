namespace CH_V01.Core.ResultExport;

public sealed class CompositeResultExporter
{
    private readonly IReadOnlyList<IResultExporter> _exporters;

    public CompositeResultExporter(IEnumerable<IResultExporter> exporters)
    {
        _exporters = exporters.ToList();
    }

    public async Task<IReadOnlyList<ResultExportResult>> ExportAsync(ResultExportContext context, CancellationToken cancellationToken = default)
    {
        var results = new List<ResultExportResult>();
        foreach (var exporter in _exporters)
        {
            results.Add(await exporter.ExportAsync(context, cancellationToken));
        }

        return results;
    }
}
