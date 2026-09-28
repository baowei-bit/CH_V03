using System.Globalization;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using CH_V03.App.Airtightness;
using CH_V03.Core.Airtightness;
using CH_V03.Core.Configuration;

namespace CH_V03.App.Views;

public partial class AirtightnessDeviceConnectionWindow : Window
{
    private readonly AirtightnessViewModel _model;
    public AirtightnessDeviceConnectionWindow(AirtightnessViewModel model)
    {
        _model = model;
        InitializeComponent();
        var ports = SerialPort.GetPortNames().OrderBy(x => x).ToArray();
        ScannerPort.ItemsSource = ports;
        InstrumentPort.ItemsSource = ports;
        var scanner = model.Configuration.Scanner;
        ScannerType.SelectedIndex = scanner.ConnectionType switch
        {
            ConnectionType.SerialPort => 1,
            ConnectionType.TcpClient => 2,
            ConnectionType.TcpServer => 3,
            ConnectionType.Disabled => 4,
            _ => 0
        };
        ScannerPort.Text = scanner.PortName;
        ScannerBaud.Text = scanner.BaudRate.ToString(CultureInfo.InvariantCulture);
        ScannerIp.Text = scanner.IpAddress;
        ScannerTcpPort.Text = scanner.Port.ToString(CultureInfo.InvariantCulture);
        ScannerConnectionTimeout.Text = scanner.TimeoutMilliseconds.ToString(CultureInfo.InvariantCulture);
        ScannerTriggerEnabled.IsChecked = model.Configuration.ScannerTriggerEnabled;
        ScannerTriggerCommand.Text = model.Configuration.ScannerTriggerCommand;
        ScannerStopCommand.Text = model.Configuration.ScannerStopCommand;
        ScannerWaitTimeout.Text = model.Configuration.ScannerWaitTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        var instrument = model.Configuration.Instrument;
        InstrumentPort.Text = instrument.PortName;
        InstrumentBaud.Text = instrument.BaudRate.ToString(CultureInfo.InvariantCulture);
        InstrumentAddress.Text = instrument.StationAddress.ToString(CultureInfo.InvariantCulture);
        InstrumentTimeout.Text = instrument.TimeoutMilliseconds.ToString(CultureInfo.InvariantCulture);
        UpdateScannerFields();
        RefreshConnectionStates();
    }

    private void ScannerType_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateScannerFields();
    private void UpdateScannerFields()
    {
        if (ScannerType is null) return;
        var serial = ScannerType.SelectedIndex == 1;
        var tcp = ScannerType.SelectedIndex is 2 or 3;
        var command = ScannerType.SelectedIndex is 1 or 2 or 3;
        ScannerSerialFields.Visibility = serial ? Visibility.Visible : Visibility.Collapsed;
        ScannerTcpFields.Visibility = tcp ? Visibility.Visible : Visibility.Collapsed;
        ScannerCommandFields.Visibility = command ? Visibility.Visible : Visibility.Collapsed;
    }
    private static int Integer(string value, string label) => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : throw new FormatException($"{label}请输入整数。");

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            DialogResult = true;
        }
        catch (Exception exception) { Error.Text = exception.Message; }
    }

    private void SaveSettings()
    {
        var scanner = ConfigurationStore.Clone(_model.Configuration.Scanner);
        scanner.ConnectionType = ScannerType.SelectedIndex switch
        {
            1 => ConnectionType.SerialPort,
            2 => ConnectionType.TcpClient,
            3 => ConnectionType.TcpServer,
            4 => ConnectionType.Disabled,
            _ => ConnectionType.UsbPosHid
        };
        scanner.PortName = ScannerPort.Text.Trim();
        scanner.BaudRate = Integer(ScannerBaud.Text, "扫码枪波特率");
        scanner.IpAddress = ScannerIp.Text.Trim();
        scanner.Port = Integer(ScannerTcpPort.Text, "扫码枪 TCP 端口");
        scanner.TimeoutMilliseconds = Integer(ScannerConnectionTimeout.Text, "扫码枪连接超时");
        var instrument = ConfigurationStore.Clone(_model.Configuration.Instrument);
        instrument.PortName = InstrumentPort.Text.Trim();
        instrument.BaudRate = Integer(InstrumentBaud.Text, "气密仪波特率");
        var address = Integer(InstrumentAddress.Text, "气密仪站号");
        if (address is < 1 or > 247) throw new FormatException("气密仪站号必须为 1–247。");
        instrument.StationAddress = (byte)address;
        instrument.TimeoutMilliseconds = Integer(InstrumentTimeout.Text, "通讯超时");
        _model.SaveDeviceConfiguration(scanner, instrument, ConfigurationStore.Clone(_model.Configuration.Motion),
            ScannerTriggerEnabled.IsChecked == true, ScannerTriggerCommand.Text, ScannerStopCommand.Text,
            Integer(ScannerWaitTimeout.Text, "扫码等待超时"));
    }

    private void RefreshConnectionStates()
    {
        ScannerConnectionState.Text = _model.IsScannerConnected ? "扫码枪已连接" : "扫码枪未连接";
        ScannerConnectionState.Foreground = _model.IsScannerConnected ? System.Windows.Media.Brushes.ForestGreen : System.Windows.Media.Brushes.SlateGray;
        ScannerTestButton.Content = _model.IsScannerConnected ? "断开扫码枪" : "连接扫码枪";
        MotionConnectionState.Text = _model.IsMotionConnected ? "I/O 模块已连接" : "I/O 模块未连接";
        MotionConnectionState.Foreground = _model.IsMotionConnected ? System.Windows.Media.Brushes.ForestGreen : System.Windows.Media.Brushes.SlateGray;
        MotionConnectionButton.Content = _model.IsMotionConnected ? "断开 I/O 模块" : "连接 I/O 模块";
    }

    private async void ToggleScanner_Click(object sender, RoutedEventArgs e)
    {
        try { SaveSettings(); await _model.ToggleScannerConnectionAsync(); RefreshConnectionStates(); }
        catch (Exception exception) { Error.Text = exception.Message; }
    }

    private async void ToggleMotion_Click(object sender, RoutedEventArgs e)
    {
        try { await _model.ToggleMotionConnectionAsync(); RefreshConnectionStates(); }
        catch (Exception exception) { Error.Text = exception.Message; }
    }

    private async Task SetMotionOutputAsync(int channel, bool enabled)
    {
        try
        {
            if (enabled && MessageBox.Show(this, $"确认开启 CH{channel} 继电器输出？", "确认动作", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            await _model.SetMotionOutputAsync(channel, enabled);
            Error.Foreground = System.Windows.Media.Brushes.ForestGreen;
            Error.Text = $"CH{channel} 已{(enabled ? "开启" : "关闭")}。";
        }
        catch (Exception exception) { Error.Foreground = System.Windows.Media.Brushes.Firebrick; Error.Text = exception.Message; }
    }

    private async void Channel1On_Click(object sender, RoutedEventArgs e) => await SetMotionOutputAsync(1, true);
    private async void Channel1Off_Click(object sender, RoutedEventArgs e) => await SetMotionOutputAsync(1, false);
    private async void Channel2On_Click(object sender, RoutedEventArgs e) => await SetMotionOutputAsync(2, true);
    private async void Channel2Off_Click(object sender, RoutedEventArgs e) => await SetMotionOutputAsync(2, false);

    private void OpenMotionSettings_Click(object sender, RoutedEventArgs e)
    {
        new AirtightnessMotionControlWindow(_model) { Owner = this }.ShowDialog();
    }
}
