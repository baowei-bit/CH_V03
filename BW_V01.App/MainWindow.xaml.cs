using System.IO;
using System.Windows;
using BW_V01.App.Services;
using BW_V01.App.ViewModels;
using BW_V01.Core.Communication;
using BW_V01.Core.Configuration;
using BW_V01.Core.ResultExport;
using BW_V01.Core.TestFlow;

namespace BW_V01.App;

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
            new CsvMesResultExporter()
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
}
