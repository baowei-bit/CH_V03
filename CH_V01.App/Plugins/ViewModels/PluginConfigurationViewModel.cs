using System.Windows.Input;
using CH_V01.App.Commands;
using CH_V01.Core.Configuration;
using CH_V01.Core.Mes;
using CH_V01.Core.ResultExport;

namespace CH_V01.App.ViewModels;

public sealed class PluginConfigurationViewModel : DialogViewModelBase
{
    private static readonly string[] DefaultExportPluginIds =
    {
        "Default.Csv",
        "Default.Dcl",
        "Default.CsvMes"
    };

    private readonly IReadOnlyDictionary<string, IMesPlugin> _mesPlugins;
    private readonly IReadOnlyDictionary<string, IResultExportPlugin> _exportPlugins;

    public PluginConfigurationViewModel(
        AppConfiguration configuration,
        IEnumerable<IMesPlugin> mesPlugins,
        IEnumerable<IResultExportPlugin> exportPlugins)
    {
        Configuration = configuration;
        _mesPlugins = mesPlugins.ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        _exportPlugins = exportPlugins.ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        MesPluginChoices = BuildMesPluginChoices(_mesPlugins.Values).ToList();
        ExportPluginChoices = BuildExportPluginChoices(_exportPlugins.Values).ToList();
        MesPluginFields = BuildMesPluginFields();
        ExportPluginFields = BuildExportPluginFields();
        SaveCommand = new RelayCommand(() => Close(true));
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public AppConfiguration Configuration { get; }

    public IReadOnlyList<PluginSelectionItemViewModel> MesPluginChoices { get; }

    public IReadOnlyList<PluginSelectionItemViewModel> ExportPluginChoices { get; }

    public IReadOnlyList<PluginConfigFieldViewModel> MesPluginFields { get; private set; }

    public IReadOnlyList<PluginConfigFieldViewModel> ExportPluginFields { get; private set; }

    public bool HasMesPluginFields => MesPluginFields.Count > 0;

    public bool HasExportPluginFields => ExportPluginFields.Count > 0;

    public string SelectedMesPluginId
    {
        get => string.Equals(Configuration.Mes.SelectedPlugin, "None", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : Configuration.Mes.SelectedPlugin;
        set
        {
            Configuration.Mes.SelectedPlugin = string.IsNullOrWhiteSpace(value) ? "None" : value;
            Configuration.Mes.Enabled = !string.IsNullOrWhiteSpace(value);
            MesPluginFields = BuildMesPluginFields();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMesPluginName));
            OnPropertyChanged(nameof(MesPluginFields));
            OnPropertyChanged(nameof(HasMesPluginFields));
        }
    }

    public string SelectedMesPluginName => MesPluginChoices
        .FirstOrDefault(item => string.Equals(item.Id, SelectedMesPluginId, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? "不加载 MES 插件";

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

            ExportPluginFields = BuildExportPluginFields();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedExportPluginName));
            OnPropertyChanged(nameof(ExportPluginFields));
            OnPropertyChanged(nameof(HasExportPluginFields));
        }
    }

    public string SelectedExportPluginName => ExportPluginChoices
        .FirstOrDefault(item => string.Equals(item.Id, SelectedExportPluginId, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? "默认导出器";

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    private IReadOnlyList<PluginConfigFieldViewModel> BuildMesPluginFields()
    {
        if (string.IsNullOrWhiteSpace(SelectedMesPluginId)
            || !_mesPlugins.TryGetValue(SelectedMesPluginId, out var plugin))
        {
            return Array.Empty<PluginConfigFieldViewModel>();
        }

        return plugin.GetConfigFields()
            .Where(field => Configuration.Compatibility.IsFieldVisible($"MesPlugin.{field.Key}"))
            .Select(field => new PluginConfigFieldViewModel(
                field.Key,
                field.DisplayName,
                field.DefaultValue,
                field.IsRequired,
                field.IsSecret,
                false,
                string.Empty,
                Configuration.Mes.PluginOptions))
            .ToList();
    }

    private IReadOnlyList<PluginConfigFieldViewModel> BuildExportPluginFields()
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
            .Where(field => Configuration.Compatibility.IsFieldVisible($"ExportPlugin.{field.Key}"))
            .Select(field => new PluginConfigFieldViewModel(
                field.Key,
                field.DisplayName,
                field.DefaultValue,
                field.IsRequired,
                field.IsSecret,
                field.IsCheckbox,
                field.HelpText,
                options))
            .ToList();
    }

    private static IEnumerable<PluginSelectionItemViewModel> BuildMesPluginChoices(IEnumerable<IMesPlugin> mesPlugins)
    {
        yield return new PluginSelectionItemViewModel
        {
            Id = string.Empty,
            DisplayName = "不加载 MES 插件"
        };

        foreach (var plugin in mesPlugins)
        {
            yield return new PluginSelectionItemViewModel
            {
                Id = plugin.Id,
                DisplayName = plugin.Name
            };
        }
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
