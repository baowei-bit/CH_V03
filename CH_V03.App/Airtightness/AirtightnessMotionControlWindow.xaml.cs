using System.Globalization;
using System.Windows;
using CH_V03.App.Airtightness;
using CH_V03.Core.Airtightness;
using CH_V03.Core.Configuration;

namespace CH_V03.App.Views;

public partial class AirtightnessMotionControlWindow : Window
{
    private readonly AirtightnessViewModel _model;
    public AirtightnessMotionControlWindow(AirtightnessViewModel model)
    {
        _model = model;
        InitializeComponent();
        var c = model.Configuration.Motion;
        Port.Text = c.PortName;
        Baud.Text = c.BaudRate.ToString(CultureInfo.InvariantCulture);
        Address.Text = c.StationAddress.ToString(CultureInfo.InvariantCulture);
        Timeout.Text = c.TimeoutMilliseconds.ToString(CultureInfo.InvariantCulture);
        Channel1Output.Text = c.Channel1Output.ToString(CultureInfo.InvariantCulture);
        Channel2Output.Text = c.Channel2Output.ToString(CultureInfo.InvariantCulture);
        Enabled.IsChecked = c.Enabled;
        AutoOpenAfterBarcode.IsChecked = c.AutoOpenAfterBarcode;
        RefreshConnectionState();
    }

    private static int Integer(string value, string label) => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : throw new FormatException($"{label}请输入整数。");
    private void RefreshConnectionState()
    {
        ConnectionState.Text = _model.IsMotionConnected ? "I/O 模块已连接" : "I/O 模块未连接";
        ConnectionState.Foreground = _model.IsMotionConnected ? System.Windows.Media.Brushes.ForestGreen : System.Windows.Media.Brushes.SlateGray;
        ConnectionButton.Content = _model.IsMotionConnected ? "断开 I/O 模块" : "连接 I/O 模块";
    }

    private AirtightnessMotionConfiguration BuildConfiguration()
    {
        var value = ConfigurationStore.Clone(_model.Configuration.Motion);
        value.Enabled = Enabled.IsChecked == true;
        value.AutoOpenAfterBarcode = AutoOpenAfterBarcode.IsChecked == true;
        value.PortName = Port.Text.Trim();
        value.BaudRate = Integer(Baud.Text, "波特率");
        var address = Integer(Address.Text, "站号");
        if (address is < 1 or > 247) throw new FormatException("站号必须为 1–247。");
        value.StationAddress = (byte)address;
        value.TimeoutMilliseconds = Integer(Timeout.Text, "通讯超时");
        value.Channel1Output = Integer(Channel1Output.Text, "CH1 输出编号");
        value.Channel2Output = Integer(Channel2Output.Text, "CH2 输出编号");
        value.Validate();
        return value;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_model.IsMotionConnected) throw new InvalidOperationException("请先断开 I/O 模块，再修改动作控制通讯或输出映射。");
            _model.SaveMotionConfiguration(BuildConfiguration());
            Error.Text = "动作设置已保存。连接模块后可进行 CH1 / CH2 人工开关验证。";
            Error.Foreground = System.Windows.Media.Brushes.ForestGreen;
        }
        catch (Exception exception) { Error.Foreground = System.Windows.Media.Brushes.Firebrick; Error.Text = exception.Message; }
    }

    private async void ToggleConnection_Click(object sender, RoutedEventArgs e)
    {
        try { await _model.ToggleMotionConnectionAsync(); RefreshConnectionState(); }
        catch (Exception exception) { Error.Foreground = System.Windows.Media.Brushes.Firebrick; Error.Text = exception.Message; }
    }

    private async Task SetOutputAsync(int channel, bool enabled)
    {
        try
        {
            if (enabled && MessageBox.Show(this, $"确认开启 CH{channel} 继电器输出？\n这会驱动现场已接线的执行器。", "确认动作", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
            await _model.SetMotionOutputAsync(channel, enabled);
            Error.Foreground = System.Windows.Media.Brushes.ForestGreen;
            Error.Text = $"CH{channel} 已{(enabled ? "开启" : "关闭")}。";
        }
        catch (Exception exception) { Error.Foreground = System.Windows.Media.Brushes.Firebrick; Error.Text = exception.Message; }
    }

    private async void Channel1On_Click(object sender, RoutedEventArgs e) => await SetOutputAsync(1, true);
    private async void Channel1Off_Click(object sender, RoutedEventArgs e) => await SetOutputAsync(1, false);
    private async void Channel2On_Click(object sender, RoutedEventArgs e) => await SetOutputAsync(2, true);
    private async void Channel2Off_Click(object sender, RoutedEventArgs e) => await SetOutputAsync(2, false);
}
