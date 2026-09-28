using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CH_V03.App.Airtightness;
using CH_V03.App.Services;
using CH_V03.Core.Airtightness;

namespace CH_V03.App.Views;

public partial class AirtightnessWindow : Window
{
    private AirtightnessLogWindow? _logWindow;
    private AirtightnessHistoryWindow? _historyWindow;

    public AirtightnessWindow(UserSession session, AirtightnessStore store)
    {
        DataContext = new AirtightnessViewModel(session, store);
        InitializeComponent();
    }

    private AirtightnessViewModel Model => (AirtightnessViewModel)DataContext;

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ShowHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_historyWindow is null)
        {
            _historyWindow = new AirtightnessHistoryWindow(Model) { Owner = this };
            _historyWindow.Closed += (_, _) => _historyWindow = null;
            _historyWindow.Show();
        }
        else
        {
            if (_historyWindow.WindowState == WindowState.Minimized) _historyWindow.WindowState = WindowState.Normal;
            _historyWindow.Activate();
        }
    }

    private async void Simulate_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Model.RunSimulationAsync();
            if (Model.IsManualScanMode) Channel1BarcodeInput.Focus();
        }
        catch (Exception exception)
        {
            Model.AddLog($"[模拟] 测试异常：{exception.Message}", "错误");
            MessageBox.Show(this, exception.Message, "模拟测试未完成", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void StartTest_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await Model.StartScanAsync();
            if (Model.IsManualScanMode) Channel1BarcodeInput.Focus();
        }
        catch (Exception exception)
        {
            Model.AddLog($"开始测试失败：{exception.Message}", "错误");
            MessageBox.Show(this, exception.Message, "开始测试失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_logWindow is null)
        {
            _logWindow = new AirtightnessLogWindow(Model) { Owner = this };
            _logWindow.Closed += (_, _) => _logWindow = null;
            _logWindow.Show();
        }
        else
        {
            if (_logWindow.WindowState == WindowState.Minimized) _logWindow.WindowState = WindowState.Normal;
            _logWindow.Activate();
        }
    }
    private void Configure_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.CanConfigure) return;
        new AirtightnessConfigurationWindow(Model) { Owner = this }.ShowDialog();
    }

    private void DeviceConnection_Click(object sender, RoutedEventArgs e)
    {
        if (!Model.CanConfigure) return;
        new AirtightnessDeviceConnectionWindow(Model) { Owner = this }.ShowDialog();
    }

    private async void ToggleScannerConnection_Click(object sender, RoutedEventArgs e)
    {
        try { await Model.ToggleScannerConnectionAsync(); }
        catch (Exception exception) { Model.AddLog($"扫码枪连接失败：{exception.Message}", "错误"); MessageBox.Show(this, exception.Message, "扫码枪连接失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ToggleInstrumentConnection_Click(object sender, RoutedEventArgs e)
    {
        try { await Model.ToggleInstrumentConnectionAsync(); }
        catch (Exception exception) { Model.AddLog($"气密仪连接失败：{exception.Message}", "错误"); MessageBox.Show(this, exception.Message, "气密仪连接失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void Channel1BarcodeInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        if (Model.AcceptBarcode(1, box.Text) == 1) Channel2BarcodeInput.Focus();
        e.Handled = true;
    }

    private void Channel2BarcodeInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox box) return;
        Model.AcceptBarcode(2, box.Text);
        e.Handled = true;
    }

    private void SwitchAccount_Click(object sender, RoutedEventArgs e)
    {
        if (Model.IsSimulating) return;
        Model.AddLog("请求切换账号，重新打开登录页面。");
        App.RestartForLogin();
    }

    protected override void OnClosed(EventArgs e)
    {
        Model.StopSimulation();
        _ = Model.DisconnectDevicesAsync();
        Model.AddLog("气密工作站已关闭。");
        base.OnClosed(e);
    }
}
