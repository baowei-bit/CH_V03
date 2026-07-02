using System.Windows.Input;
using BW_V01.App.Commands;
using BW_V01.Core.Configuration;
using BW_V01.Core.Mes;

namespace BW_V01.App.ViewModels;

public sealed class MesConfigurationViewModel : DialogViewModelBase
{
    private readonly IReadOnlyDictionary<string, IMesPlugin> _mesPlugins;

    public MesConfigurationViewModel(AppConfiguration configuration, IEnumerable<IMesPlugin> mesPlugins)
    {
        Configuration = configuration;
        _mesPlugins = mesPlugins.ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        MesPluginChoices = BuildMesPluginChoices(_mesPlugins.Values).ToList();
        PluginFields = BuildPluginFields();
        SaveCommand = new RelayCommand(() => Close(true));
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public AppConfiguration Configuration { get; }

    public IReadOnlyList<PluginSelectionItemViewModel> MesPluginChoices { get; }

    public IReadOnlyList<PluginConfigFieldViewModel> PluginFields { get; private set; }

    public bool HasPluginFields => PluginFields.Count > 0;

    public string SelectedMesPluginId
    {
        get => string.Equals(Configuration.Mes.SelectedPlugin, "None", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : Configuration.Mes.SelectedPlugin;
        set
        {
            Configuration.Mes.SelectedPlugin = string.IsNullOrWhiteSpace(value) ? "None" : value;
            Configuration.Mes.Enabled = !string.IsNullOrWhiteSpace(value);
            PluginFields = BuildPluginFields();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMesPluginName));
            OnPropertyChanged(nameof(PluginFields));
            OnPropertyChanged(nameof(HasPluginFields));
        }
    }

    public string SelectedMesPluginName => MesPluginChoices
        .FirstOrDefault(item => string.Equals(item.Id, SelectedMesPluginId, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? "不加载 MES 插件";

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    private IReadOnlyList<PluginConfigFieldViewModel> BuildPluginFields()
    {
        if (string.IsNullOrWhiteSpace(SelectedMesPluginId)
            || !_mesPlugins.TryGetValue(SelectedMesPluginId, out var plugin))
        {
            return Array.Empty<PluginConfigFieldViewModel>();
        }

        return plugin.GetConfigFields()
            .Select(field => new PluginConfigFieldViewModel(
                field.Key,
                field.DisplayName,
                field.DefaultValue,
                field.IsRequired,
                field.IsSecret,
                Configuration.Mes.PluginOptions))
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
}
