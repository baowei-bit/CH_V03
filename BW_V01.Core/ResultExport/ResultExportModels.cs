using BW_V01.Core.Configuration;
using BW_V01.Core.TestFlow;

namespace BW_V01.Core.ResultExport;

public sealed class ResultExportContext
{
    public AppConfiguration Configuration { get; init; } = new();

    public TestFlowSnapshot Snapshot { get; init; } = new();

    public IReadOnlyDictionary<string, string> PluginOptions { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public string Guid { get; init; } = System.Guid.NewGuid().ToString();

    public string ResultText => Snapshot.IsPass == true ? "PASS" : "FAIL";

    public string ResultTextNgOk => Snapshot.IsPass == true ? "OK" : "NG";

    public string FirstFailItem => Snapshot.FirstFailItem;

    public string FirstFailValue => Snapshot.FirstFailValue;

    public DateTime StartTime => Snapshot.StartTime ?? DateTime.Now;

    public DateTime EndTime => Snapshot.EndTime ?? DateTime.Now;

    public double TestSeconds => Math.Round(Snapshot.Elapsed.TotalSeconds, 2);

    public string GetOption(string key, string defaultValue = "")
    {
        if (PluginOptions.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        if (Configuration.Mes.PluginOptions.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return defaultValue;
    }
}

public sealed class ResultExportResult
{
    public bool IsSuccess { get; init; }

    public string ExporterName { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string FilePath { get; init; } = string.Empty;

    public static ResultExportResult Success(string exporterName, string message, string filePath)
    {
        return new ResultExportResult
        {
            IsSuccess = true,
            ExporterName = exporterName,
            Message = message,
            FilePath = filePath
        };
    }

    public static ResultExportResult Fail(string exporterName, string message)
    {
        return new ResultExportResult
        {
            IsSuccess = false,
            ExporterName = exporterName,
            Message = message
        };
    }
}
