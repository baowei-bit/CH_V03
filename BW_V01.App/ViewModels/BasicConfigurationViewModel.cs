using System.Windows.Input;
using BW_V01.App.Commands;
using BW_V01.Core.Configuration;
using BW_V01.Core.ResultExport;

namespace BW_V01.App.ViewModels;

public sealed class BasicConfigurationViewModel : DialogViewModelBase
{
    private static readonly string[] DefaultExportPluginIds =
    {
        "Default.Csv",
        "Default.Dcl",
        "Default.CsvMes"
    };

    private readonly IReadOnlyDictionary<string, IResultExportPlugin> _exportPlugins;

    public BasicConfigurationViewModel(AppConfiguration configuration, IEnumerable<IResultExportPlugin> exportPlugins)
    {
        Configuration = configuration;
        _exportPlugins = exportPlugins.ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        ExportPluginChoices = BuildExportPluginChoices(_exportPlugins.Values).ToList();
        PluginFields = BuildPluginFields();
        SaveCommand = new RelayCommand(() => Close(true));
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public AppConfiguration Configuration { get; }

    public IReadOnlyList<PluginSelectionItemViewModel> ExportPluginChoices { get; }

    public IReadOnlyList<PluginConfigFieldViewModel> PluginFields { get; private set; }

    public bool HasPluginFields => PluginFields.Count > 0;

    public string SelectedExportPluginId
    {
        get
        {
            var enabled = Configuration.Export.EnabledPluginIds;
            if (enabled.Count == 0 || IsDefaultExport(enabled))
            {
                return string.Empty;
            }

            return enabled.FirstOrDefault() ?? string.Empty;
        }
        set
        {
            Configuration.Export.EnabledPluginIds.Clear();
            if (string.IsNullOrWhiteSpace(value))
            {
                Configuration.Export.EnabledPluginIds.AddRange(DefaultExportPluginIds);
            }
            else
            {
                Configuration.Export.EnabledPluginIds.Add(value);
            }

            PluginFields = BuildPluginFields();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedExportPluginName));
            OnPropertyChanged(nameof(PluginFields));
            OnPropertyChanged(nameof(HasPluginFields));
        }
    }

    public string SelectedExportPluginName => ExportPluginChoices
        .FirstOrDefault(item => string.Equals(item.Id, SelectedExportPluginId, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? "默认导出器";

    public string[] ConnectionTypes { get; } = Enum.GetNames<ConnectionType>();

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    private IReadOnlyList<PluginConfigFieldViewModel> BuildPluginFields()
    {
        var pluginId = SelectedExportPluginId;
        if (string.IsNullOrWhiteSpace(pluginId) || !_exportPlugins.TryGetValue(pluginId, out var plugin))
        {
            return Array.Empty<PluginConfigFieldViewModel>();
        }

        if (!Configuration.Export.PluginOptions.TryGetValue(pluginId, out var options))
        {
            options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Configuration.Export.PluginOptions[pluginId] = options;
        }

        return plugin.GetConfigFields()
            .Select(field => new PluginConfigFieldViewModel(
                field.Key,
                field.DisplayName,
                field.DefaultValue,
                field.IsRequired,
                field.IsSecret,
                options))
            .ToList();
    }

    private static IEnumerable<PluginSelectionItemViewModel> BuildExportPluginChoices(IEnumerable<IResultExportPlugin> exportPlugins)
    {
        yield return new PluginSelectionItemViewModel
        {
            Id = string.Empty,
            DisplayName = "默认导出器（CSV + DCL + CsvMes）"
        };

        foreach (var plugin in exportPlugins.Where(plugin => !plugin.Id.StartsWith("Default.", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new PluginSelectionItemViewModel
            {
                Id = plugin.Id,
                DisplayName = plugin.Name
            };
        }
    }

    private static bool IsDefaultExport(IReadOnlyCollection<string> enabledPluginIds)
    {
        return enabledPluginIds.Count == DefaultExportPluginIds.Length
            && DefaultExportPluginIds.All(defaultId => enabledPluginIds.Contains(defaultId, StringComparer.OrdinalIgnoreCase));
    }
}
