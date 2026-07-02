using BW_V01.Core.Configuration;
using BW_V01.Core.ResultExport;
using BW_V01.Core.TestFlow;

namespace BW_V01.App.Services;

public sealed class ExportRuntimeService
{
    private readonly IReadOnlyList<IResultExportPlugin> _plugins;

    public ExportRuntimeService(string pluginDirectory, IEnumerable<IResultExportPlugin> builtInPlugins)
    {
        PluginDirectory = pluginDirectory;
        var externalPlugins = new ResultExportPluginLoader().LoadFromDirectory(pluginDirectory);
        _plugins = builtInPlugins
            .Concat(externalPlugins)
            .GroupBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(plugin => plugin.Name)
            .ToList();
    }

    public string PluginDirectory { get; }

    public IReadOnlyList<IResultExportPlugin> Plugins => _plugins;

    public async Task<IReadOnlyList<ResultExportResult>> ExportAsync(AppConfiguration configuration, TestFlowSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var results = new List<ResultExportResult>();
        foreach (var pluginId in configuration.Export.EnabledPluginIds.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var plugin = _plugins.FirstOrDefault(item => string.Equals(item.Id, pluginId, StringComparison.OrdinalIgnoreCase));
            if (plugin is null)
            {
                results.Add(ResultExportResult.Fail(pluginId, $"未找到导出插件：{pluginId}"));
                continue;
            }

            var options = configuration.Export.GetOptions(plugin.Id);
            var context = new ResultExportContext
            {
                Configuration = configuration,
                Snapshot = snapshot,
                PluginOptions = options
            };

            results.Add(await plugin.ExportAsync(context, cancellationToken));
        }

        return results;
    }
}
