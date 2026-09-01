using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CH_V01.App.Communication;
using CH_V01.App.Services;
using CH_V01.App.ViewModels;
using CH_V01.Core.Communication;
using CH_V01.Core.Configuration;
using CH_V01.Core.ResultExport;
using CH_V01.Core.TestFlow;

namespace CH_V01.App;

public partial class MainWindow : Window
{
    private static readonly TimeSpan KeyboardScannerIdleTimeout = TimeSpan.FromMilliseconds(160);
    private readonly StringBuilder _keyboardScannerBuffer = new();
    private readonly DispatcherTimer _keyboardScannerFlushTimer;

    public MainWindow()
    {
        _keyboardScannerFlushTimer = new DispatcherTimer
        {
            Interval = KeyboardScannerIdleTimeout
        };
        _keyboardScannerFlushTimer.Tick += KeyboardScannerFlushTimer_Tick;

        InitializeComponent();
        PreviewTextInput += MainWindow_PreviewTextInput;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        var store = new ConfigurationStore(AppPaths.ConfigurationFile);
        static IDeviceConnection CreateUsbPosHidConnection(DeviceConnectionConfiguration configuration) =>
            new UsbPosHidDeviceConnection(configuration);

        var communicationTestService = new CommunicationTestService(CreateUsbPosHidConnection);
        var deviceConnectionFactory = new DeviceConnectionFactory(CreateUsbPosHidConnection);
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

    protected override void OnClosed(EventArgs e)
    {
        _keyboardScannerFlushTimer.Stop();
        _keyboardScannerFlushTimer.Tick -= KeyboardScannerFlushTimer_Tick;
        PreviewTextInput -= MainWindow_PreviewTextInput;
        PreviewKeyDown -= MainWindow_PreviewKeyDown;
        base.OnClosed(e);
    }

    private void MainWindow_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!ShouldCaptureUsbPosHidKeyboardInput() || string.IsNullOrEmpty(e.Text))
        {
            ResetKeyboardScannerBuffer();
            return;
        }

        foreach (var character in e.Text)
        {
            if (!char.IsControl(character))
            {
                _keyboardScannerBuffer.Append(character);
            }
        }

        if (_keyboardScannerBuffer.Length == 0)
        {
            return;
        }

        e.Handled = true;
        _keyboardScannerFlushTimer.Stop();
        _keyboardScannerFlushTimer.Start();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!ShouldCaptureUsbPosHidKeyboardInput())
        {
            ResetKeyboardScannerBuffer();
            return;
        }

        if (e.Key is Key.Enter or Key.Return)
        {
            if (_keyboardScannerBuffer.Length > 0)
            {
                e.Handled = true;
                FlushKeyboardScannerBuffer();
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            ResetKeyboardScannerBuffer();
        }
    }

    private void KeyboardScannerFlushTimer_Tick(object? sender, EventArgs e)
    {
        FlushKeyboardScannerBuffer();
    }

    private bool ShouldCaptureUsbPosHidKeyboardInput()
    {
        return DataContext is MainWindowViewModel { IsUsbPosHidScanner: true };
    }

    private void FlushKeyboardScannerBuffer()
    {
        _keyboardScannerFlushTimer.Stop();
        if (!ShouldCaptureUsbPosHidKeyboardInput() || _keyboardScannerBuffer.Length == 0)
        {
            _keyboardScannerBuffer.Clear();
            return;
        }

        var barcode = _keyboardScannerBuffer.ToString();
        _keyboardScannerBuffer.Clear();
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.AcceptUsbPosHidKeyboardInputAsync(barcode);
        }
    }

    private void ResetKeyboardScannerBuffer()
    {
        _keyboardScannerFlushTimer.Stop();
        _keyboardScannerBuffer.Clear();
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
