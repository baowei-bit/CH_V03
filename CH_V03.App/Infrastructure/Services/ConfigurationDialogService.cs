using System.Windows;
using CH_V03.App.ViewModels;
using CH_V03.App.Views;
using CH_V03.Core.Communication;
using CH_V03.Core.Configuration;
using CH_V03.Core.Mes;
using CH_V03.Core.ResultExport;

namespace CH_V03.App.Services;

public sealed class ConfigurationDialogService : IConfigurationDialogService
{
    private readonly Window _owner;
    private readonly IReadOnlyList<IMesPlugin> _mesPlugins;
    private readonly IReadOnlyList<IResultExportPlugin> _exportPlugins;
    private readonly CH_V03.App.Communication.EndoscopeScannerService _endoscopeScannerService;

    public ConfigurationDialogService(Window owner, IEnumerable<IMesPlugin> mesPlugins, IEnumerable<IResultExportPlugin> exportPlugins, CH_V03.App.Communication.EndoscopeScannerService endoscopeScannerService)
    {
        _owner = owner;
        _mesPlugins = mesPlugins.ToList();
        _exportPlugins = exportPlugins.ToList();
        _endoscopeScannerService = endoscopeScannerService;
    }

    public bool ShowBasicConfiguration(AppConfiguration configuration, bool canManageAllSettings)
    {
        var viewModel = new BasicConfigurationViewModel(configuration, _exportPlugins, canManageAllSettings);
        var window = new BasicConfigurationWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        return window.ShowDialog() == true;
    }

    public bool ShowKunshanJiaLianYiBasicConfiguration(AppConfiguration configuration, KunshanJiaLianYiRuntimeActions runtimeActions)
    {
        var viewModel = new KunshanJiaLianYiBasicConfigurationViewModel(configuration, runtimeActions);
        var window = new KunshanJiaLianYiBasicConfigurationWindow { Owner = _owner, DataContext = viewModel };
        BindCloseRequest(window, viewModel);
        return window.ShowDialog() == true;
    }

    public bool ShowCommunicationConfiguration(AppConfiguration configuration, CommunicationTestService communicationTestService)
    {
        var viewModel = new CommunicationConfigurationViewModel(configuration, communicationTestService);
        var window = new CommunicationConfigurationWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        return window.ShowDialog() == true;
    }

    public bool ShowEndoscope(AppConfiguration configuration)
    {
        var window = new EndoscopeWindow(_endoscopeScannerService, configuration.Communication.Scanner)
        {
            Owner = _owner
        };
        return window.ShowDialog() == true;
    }

    public bool ShowMesConfiguration(AppConfiguration configuration)
    {
        var viewModel = new MesConfigurationViewModel(configuration);
        var window = new MesConfigurationWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        return window.ShowDialog() == true;
    }

    public void ShowMesDebugTool(AppConfiguration configuration)
    {
        var viewModel = new MesDebugToolViewModel(configuration);
        var window = new MesDebugToolWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        window.ShowDialog();
    }

    public bool ShowPluginConfiguration(AppConfiguration configuration)
    {
        var viewModel = new PluginConfigurationViewModel(configuration, _mesPlugins, _exportPlugins);
        var window = new PluginConfigurationWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        return window.ShowDialog() == true;
    }

    public bool ShowKunshanJiaLianYiConfiguration(AppConfiguration configuration)
    {
        var viewModel = new KunshanJiaLianYiConfigurationViewModel(configuration);
        var window = new KunshanJiaLianYiConfigurationWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        return window.ShowDialog() == true;
    }

    public bool ShowTestItemConfiguration(TestItemConfigurationSet configuration)
    {
        var viewModel = new TestItemConfigurationViewModel(configuration);
        var window = new TestItemConfigurationWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        return window.ShowDialog() == true;
    }

    public bool ShowUserConfiguration(UserConfiguration configuration, CompatibilityConfiguration compatibility, bool canManageAccounts)
    {
        var viewModel = new UserConfigurationViewModel(configuration, compatibility, canManageAccounts);
        var window = new UserConfigurationWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        return window.ShowDialog() == true;
    }

    public bool ShowKunshanJiaLianYiUserConfiguration(UserConfiguration configuration, CompatibilityConfiguration compatibility)
    {
        var viewModel = new UserConfigurationViewModel(configuration, compatibility, canManageAccounts: false);
        var window = new KunshanJiaLianYiUserConfigurationWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        return window.ShowDialog() == true;
    }

    public bool ShowProjectProfileImport(IReadOnlyList<ProjectProfile> profiles, out ProjectProfile? selectedProfile)
    {
        var viewModel = new ProjectProfileImportViewModel(profiles);
        var window = new ProjectProfileImportWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        var result = window.ShowDialog() == true;
        selectedProfile = result ? viewModel.SelectedProfile?.Profile : null;
        return result && selectedProfile is not null;
    }

    public bool ShowLogin(AuthenticationService authenticationService, UserConfiguration configuration)
    {
        var viewModel = new LoginViewModel(authenticationService, configuration);
        var window = new LoginWindow
        {
            DataContext = viewModel,
            WindowStartupLocation = _owner.IsVisible ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen
        };
        if (_owner.IsVisible)
        {
            window.Owner = _owner;
        }

        BindCloseRequest(window, viewModel);
        return window.ShowDialog() == true;
    }

    public void ShowDataAnalysis(IEnumerable<ProductionRecordViewModel> records, int passCount, int failCount, int currentConsecutiveNg)
    {
        var viewModel = new DataAnalysisViewModel(records, passCount, failCount, currentConsecutiveNg);
        var window = new DataAnalysisWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        window.ShowDialog();
    }

    public void ShowHelp(AppConfiguration configuration)
    {
        var viewModel = new HelpViewModel(configuration.Identity.ProductName, configuration.Mes.SelectedPlugin);
        var window = new HelpWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

        BindCloseRequest(window, viewModel);
        window.ShowDialog();
    }

    public void ShowAlert(string title, string message)
    {
        MessageBox.Show(_owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private static void BindCloseRequest(Window window, DialogViewModelBase viewModel)
    {
        viewModel.RequestClose += (_, result) =>
        {
            window.DialogResult = result;
            window.Close();
        };
    }
}
