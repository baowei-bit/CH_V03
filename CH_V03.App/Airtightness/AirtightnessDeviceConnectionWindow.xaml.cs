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
        MotionPort.ItemsSource = ports;

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

        var motion = model.Configuration.Motion;
        MotionPort.Text = motion.PortName;
        MotionBaud.Text = motion.BaudRate.ToString(CultureInfo.InvariantCulture);
        MotionAddress.Text = motion.StationAddress.ToString(CultureInfo.InvariantCulture);
        MotionTimeout.Text = motion.TimeoutMilliseconds.ToString(CultureInfo.InvariantCulture);
        MotionChannel1Output.Text = motion.Channel1Output.ToString(CultureInfo.InvariantCulture);
        MotionChannel2Output.Text = motion.Channel2Output.ToString(CultureInfo.InvariantCulture);
        MotionStartInput.Text = motion.StartInput.ToString(CultureInfo.InvariantCulture);
        MotionOutputReleaseDelay.Text = motion.OutputReleaseDelaySeconds.ToString(CultureInfo.InvariantCulture);

        UpdateScannerFields();
        RefreshConnectionStates();
        Loaded += (_, _) => FitIntoWorkingArea();
    }

    private void FitIntoWorkingArea()
    {
        var area = SystemParameters.WorkArea;
        Width = Math.Min(1120, Math.Max(680, area.Width - 32));
        Height = Math.Min(760, Math.Max(520, area.Height - 32));
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Top + (area.Height - ActualHeight) / 2;
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

    private static int Integer(string value, string label) => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
        ? parsed : throw new FormatException($"{label}请输入整数。");

    private static decimal Decimal(string value, string label) => decimal.TryParse(value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
        ? parsed : throw new FormatException($"{label}请输入有效数值。");

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveSettings();
            DialogResult = true;
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private void SaveSettings()
    {
        if (_model.IsScannerConnected || _model.IsInstrumentConnected || _model.IsMotionConnected)
            throw new InvalidOperationException("请先断开已连接的设备，再修改通讯参数或下位机映射。");

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
        var instrumentAddress = Integer(InstrumentAddress.Text, "气密仪站号");
        if (instrumentAddress is < 1 or > 247) throw new FormatException("气密仪站号必须为 1–247。");
        instrument.StationAddress = (byte)instrumentAddress;
        instrument.TimeoutMilliseconds = Integer(InstrumentTimeout.Text, "气密仪通讯超时");

        var motion = ConfigurationStore.Clone(_model.Configuration.Motion);
        motion.PortName = MotionPort.Text.Trim();
        motion.BaudRate = Integer(MotionBaud.Text, "下位机波特率");
        var motionAddress = Integer(MotionAddress.Text, "下位机站号");
        if (motionAddress is < 1 or > 247) throw new FormatException("下位机站号必须为 1–247。");
        motion.StationAddress = (byte)motionAddress;
        motion.TimeoutMilliseconds = Integer(MotionTimeout.Text, "下位机通讯超时");
        motion.Channel1Output = Integer(MotionChannel1Output.Text, "CH1 输出编号");
        motion.Channel2Output = Integer(MotionChannel2Output.Text, "CH2 输出编号");
        motion.StartInput = Integer(MotionStartInput.Text, "启动输入编号");
        motion.OutputReleaseDelaySeconds = Decimal(MotionOutputReleaseDelay.Text, "测试结束后 Y 保持时间");
        motion.Validate();

        _model.SaveDeviceConfiguration(scanner, instrument, motion,
            ScannerTriggerEnabled.IsChecked == true, ScannerTriggerCommand.Text, ScannerStopCommand.Text,
            Integer(ScannerWaitTimeout.Text, "扫码等待超时"));
    }

    private void RefreshConnectionStates()
    {
        SetConnectionState(ScannerConnectionState, _model.IsScannerConnected, "扫码枪");
        ScannerConnectionButton.Content = _model.IsScannerConnected ? "断开扫码枪" : "连接扫码枪";
        SetConnectionState(InstrumentConnectionState, _model.IsInstrumentConnected, "气密仪");
        InstrumentConnectionButton.Content = _model.IsInstrumentConnected ? "断开气密仪" : "连接气密仪";
        SetConnectionState(MotionConnectionState, _model.IsMotionConnected, "下位机");
        MotionConnectionButton.Content = _model.IsMotionConnected ? "断开下位机" : "连接下位机";
    }

    private static void SetConnectionState(TextBlock target, bool connected, string device)
    {
        target.Text = connected ? $"{device}已连接" : $"{device}未连接";
        target.Foreground = connected ? System.Windows.Media.Brushes.ForestGreen : System.Windows.Media.Brushes.SlateGray;
    }

    private void ShowSuccess(string text)
    {
        Error.Foreground = System.Windows.Media.Brushes.ForestGreen;
        Error.Text = text;
    }

    private void ShowError(string text)
    {
        Error.Foreground = System.Windows.Media.Brushes.Firebrick;
        Error.Text = text;
    }

    private async void ToggleScanner_Click(object sender, RoutedEventArgs e)
    {
        try { if (!_model.IsScannerConnected && !_model.IsInstrumentConnected && !_model.IsMotionConnected) { SaveSettings(); } await _model.ToggleScannerConnectionAsync(); RefreshConnectionStates(); }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private async void ToggleInstrument_Click(object sender, RoutedEventArgs e)
    {
        try { if (!_model.IsScannerConnected && !_model.IsInstrumentConnected && !_model.IsMotionConnected) { SaveSettings(); } await _model.ToggleInstrumentConnectionAsync(); RefreshConnectionStates(); }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private async void ToggleMotion_Click(object sender, RoutedEventArgs e)
    {
        try { if (!_model.IsScannerConnected && !_model.IsInstrumentConnected && !_model.IsMotionConnected) { SaveSettings(); } await _model.ToggleMotionConnectionAsync(); RefreshConnectionStates(); }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private async void TestScanner_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_model.IsScannerConnected && !_model.IsInstrumentConnected && !_model.IsMotionConnected) SaveSettings();
            await _model.TestScannerConnectionAsync();
            ShowSuccess("扫码枪测试连通成功：当前通讯方式可正常打开。");
        }
        catch (Exception exception) { ShowError($"扫码枪测试连通失败：{exception.Message}"); }
    }

    private async void TestInstrument_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_model.IsScannerConnected && !_model.IsInstrumentConnected && !_model.IsMotionConnected) SaveSettings();
            var pressure = await _model.TestInstrumentConnectionAsync();
            ShowSuccess($"气密仪测试连通成功：当前压力 {pressure:0.000} kPa。");
        }
        catch (Exception exception) { ShowError($"气密仪测试连通失败：{exception.Message}"); }
    }

    private async void TestMotion_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_model.IsScannerConnected && !_model.IsInstrumentConnected && !_model.IsMotionConnected) SaveSettings();
            var startInput = await _model.TestMotionConnectionAsync();
            ShowSuccess($"下位机测试连通成功：启动输入为{(startInput ? "触发" : "未触发")}。未查询继电器状态。");
        }
        catch (Exception exception) { ShowError($"下位机测试连通失败：{exception.Message}"); }
    }

    private async Task SetMotionOutputAsync(int channel, bool enabled)
    {
        try
        {
            if (enabled && MessageBox.Show(this, $"确认开启 CH{channel} 下位机输出？\n这会驱动现场已接线的执行器。", "确认动作", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            await _model.SetMotionOutputAsync(channel, enabled);
            ShowSuccess($"CH{channel} 已{(enabled ? "开启" : "关闭")}。");
        }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private async void Channel1On_Click(object sender, RoutedEventArgs e) => await SetMotionOutputAsync(1, true);
    private async void Channel1Off_Click(object sender, RoutedEventArgs e) => await SetMotionOutputAsync(1, false);
    private async void Channel2On_Click(object sender, RoutedEventArgs e) => await SetMotionOutputAsync(2, true);
    private async void Channel2Off_Click(object sender, RoutedEventArgs e) => await SetMotionOutputAsync(2, false);
}
