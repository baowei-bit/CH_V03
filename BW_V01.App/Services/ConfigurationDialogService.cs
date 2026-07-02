using System.Windows;
using BW_V01.App.ViewModels;
using BW_V01.App.Views;
using BW_V01.Core.Communication;
using BW_V01.Core.Configuration;
using BW_V01.Core.Mes;
using BW_V01.Core.ResultExport;

namespace BW_V01.App.Services;

public sealed class ConfigurationDialogService : IConfigurationDialogService
{
    private readonly Window _owner;
    private readonly IReadOnlyList<IMesPlugin> _mesPlugins;
    private readonly IReadOnlyList<IResultExportPlugin> _exportPlugins;

    public ConfigurationDialogService(Window owner, IEnumerable<IMesPlugin> mesPlugins, IEnumerable<IResultExportPlugin> exportPlugins)
    {
        _owner = owner;
        _mesPlugins = mesPlugins.ToList();
        _exportPlugins = exportPlugins.ToList();
    }

    public bool ShowBasicConfiguration(AppConfiguration configuration)
    {
        var viewModel = new BasicConfigurationViewModel(configuration, _exportPlugins);
        var window = new BasicConfigurationWindow
        {
            Owner = _owner,
            DataContext = viewModel
        };

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

    public bool ShowMesConfiguration(AppConfiguration configuration)
    {
        var viewModel = new MesConfigurationViewModel(configuration, _mesPlugins);
        var window = new MesConfigurationWindow
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

    public bool ShowUserConfiguration(UserConfiguration configuration)
    {
        var viewModel = new UserConfigurationViewModel(configuration);
        var window = new UserConfigurationWindow
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
        var viewModel = new HelpViewModel(configuration.Identity.ProductName, configuration.Identity.CustomerName, configuration.Mes.SelectedPlugin);
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
