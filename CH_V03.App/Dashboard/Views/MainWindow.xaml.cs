using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CH_V03.App.Communication;
using CH_V03.App.Services;
using CH_V03.App.ViewModels;
using CH_V03.Core.Communication;
using CH_V03.Core.Configuration;
using CH_V03.Core.ResultExport;
using CH_V03.Core.TestFlow;

namespace CH_V03.App;

public partial class MainWindow : Window
{
    private static readonly TimeSpan KeyboardScannerIdleTimeout = TimeSpan.FromMilliseconds(160);
    private readonly StringBuilder _keyboardScannerBuffer = new();
    private readonly DispatcherTimer _keyboardScannerFlushTimer;
    private bool _closingAfterMesLogout;

    public MainWindow(AuthenticationService? authenticatedService = null)
    {
        if (authenticatedService is not null && (!authenticatedService.CurrentSession.IsAuthenticated ||
            authenticatedService.CurrentSession.Module != TestModule.Electrical))
            throw new UnauthorizedAccessException("只有电测账号可以打开电测工作站。");
        _keyboardScannerFlushTimer = new DispatcherTimer
        {
            Interval = KeyboardScannerIdleTimeout
        };
        _keyboardScannerFlushTimer.Tick += KeyboardScannerFlushTimer_Tick;

        InitializeComponent();
        PreviewTextInput += MainWindow_PreviewTextInput;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        var store = new ConfigurationStore(AppPaths.ConfigurationFile);
        var jingWang = string.Equals(authenticatedService?.CurrentSession.UserName, "jingwang", StringComparison.OrdinalIgnoreCase);
        if (jingWang)
        {
            var profile = new ConfigurationStore(Path.Combine(Path.GetDirectoryName(store.FilePath)!, "jingwang.json"));
            if (!File.Exists(profile.FilePath)) profile.Save(store.Load());
            store = profile;
        }
        var endoscopeScannerService = new EndoscopeScannerService();
        static IDeviceConnection CreateUsbPosHidConnection(DeviceConnectionConfiguration configuration) =>
            new UsbPosHidDeviceConnection(configuration);
        IDeviceConnection CreateCameraQrConnection(DeviceConnectionConfiguration configuration) =>
            new CameraQrDeviceConnection(configuration, endoscopeScannerService);

        // A connection test must not reconfigure or close the production camera service.
        var communicationTestService = new CommunicationTestService(CreateUsbPosHidConnection,
            configuration => new CameraQrDeviceConnection(configuration, new EndoscopeScannerService()));
        var deviceConnectionFactory = new DeviceConnectionFactory(CreateUsbPosHidConnection, CreateCameraQrConnection);
        var protocolAdapter = new DefaultIctProtocolAdapter();
        var testFlowService = new TestFlowService(protocolAdapter);

        var exportPluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins", "export");
        var exportRuntimeService = new ExportRuntimeService(exportPluginDirectory, new IResultExportPlugin[]
        {
            new CsvResultExporter(),
            new Yidong2Exporter(),
            new DclResultExporter(),
            new CsvMesResultExporter(),
            new CustomCsvResultExporter()
        });

        var mesPluginDirectory = Path.Combine(AppContext.BaseDirectory, "plugins", "mes");
        var mesRuntimeService = new MesRuntimeService(mesPluginDirectory);

        var dialogService = new ConfigurationDialogService(this, mesRuntimeService.Plugins, exportRuntimeService.Plugins, endoscopeScannerService);
        var authenticationService = authenticatedService ?? new AuthenticationService();
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
        if (DataContext is MainWindowViewModel model) model.DisconnectCompanion();
        _keyboardScannerFlushTimer.Stop();
        _keyboardScannerFlushTimer.Tick -= KeyboardScannerFlushTimer_Tick;
        PreviewTextInput -= MainWindow_PreviewTextInput;
        PreviewKeyDown -= MainWindow_PreviewKeyDown;
        base.OnClosed(e);
    }

    protected override async void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (DataContext is MainWindowViewModel maintenanceModel && maintenanceModel.CompanionMaintenanceBusy)
        {
            e.Cancel = true;
            MessageBox.Show(this, "请等待陪测板烧录或写入完成后关闭程序。", "陪测板维护");
            return;
        }
        if (!_closingAfterMesLogout && DataContext is MainWindowViewModel viewModel)
        {
            e.Cancel = true;
            _closingAfterMesLogout = true;
            try { await viewModel.EndJingWangOnExitAsync(); }
            catch (Exception) { /* 退出时的下机失败已由会话日志记录，窗口仍需关闭。 */ }
            finally { _ = Dispatcher.BeginInvoke(new Action(Close)); }
            return;
        }
        base.OnClosing(e);
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

    private async void KeyboardSn_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.IsRepeat || DataContext is not MainWindowViewModel model) return;
        e.Handled = true;
        var input = (System.Windows.Controls.TextBox)sender;
        await model.SubmitKeyboardSnAsync(input.Text);
        input.SelectAll();
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
        return CompanionTab.IsSelected == false
            && Keyboard.FocusedElement is not System.Windows.Controls.TextBox
            && DataContext is MainWindowViewModel { IsUsbPosHidScanner: true };
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
