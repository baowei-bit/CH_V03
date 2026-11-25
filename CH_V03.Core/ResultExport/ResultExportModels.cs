using CH_V03.Core.Configuration;
using CH_V03.Core.TestFlow;

namespace CH_V03.Core.ResultExport;

public sealed class ResultExportContext
{
    public AppConfiguration Configuration { get; init; } = new();

    public TestFlowSnapshot Snapshot { get; init; } = new();

    public bool IsJingWangAccount { get; init; }

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

/// <summary>统一处理嘉联益调试模式下的结果文件命名。</summary>
public static class ResultExportFileName
{
    public static string ApplyTestPieceSuffix(ResultExportContext context, string fileName)
    {
        return ApplyTestPieceSuffix(context.Configuration, fileName);
    }

    public static string ApplyTestPieceSuffix(AppConfiguration configuration, string fileName)
    {
        if (!configuration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration
            || !configuration.User.Jly.DebugMode || string.IsNullOrWhiteSpace(fileName))
        {
            return fileName;
        }

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        return name.EndsWith("_debug", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : $"{name}_debug{extension}";
    }
}
