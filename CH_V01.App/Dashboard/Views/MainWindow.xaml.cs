using System.IO;
using System.Windows;
using CH_V01.App.Services;
using CH_V01.App.ViewModels;
using CH_V01.Core.Communication;
using CH_V01.Core.Configuration;
using CH_V01.Core.ResultExport;
using CH_V01.Core.TestFlow;

namespace CH_V01.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var store = new ConfigurationStore(AppPaths.ConfigurationFile);
        var communicationTestService = new CommunicationTestService();
        var deviceConnectionFactory = new DeviceConnectionFactory();
        var protocolAdapter = new DefaultIctProtocolAdapter();
        var testFlowService = new TestFlowService(protocolAdapter);

        var exportPluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins", "export");
        var exportRuntimeService = new ExportRuntimeService(exportPluginDirectory, new IResultExportPlugin[]
        {
            new CsvResultExporter(),
            new DclResultExporter(),
            new CsvMesResultExporter(),
            new CustomCsvResultExporter()
        });

        var mesPluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins", "mes");
        var mesRuntimeService = new MesRuntimeService(mesPluginDirectory);
        var dialogService = new ConfigurationDialogService(this, mesRuntimeService.Plugins, exportRuntimeService.Plugins);
        var authenticationService = new AuthenticationService();
        var projectProfiles = new ProjectProfileStore(AppPaths.ProjectProfilesDirectory).LoadAll();

        var viewModel = new MainWindowViewModel(
            store,
            communicationTestService,
            deviceConnectionFactory,
            dialogService,
            protocolAdapter,
            testFlowService,
            exportRuntimeService,
            mesRuntimeService,
            authenticationService,
            projectProfiles);
        DataContext = viewModel;
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
