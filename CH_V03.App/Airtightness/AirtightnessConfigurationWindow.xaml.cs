using System.Globalization;
using System.Windows;
using CH_V03.App.Airtightness;
using CH_V03.Core.Airtightness;
using CH_V03.Core.Configuration;

namespace CH_V03.App.Views;

public partial class AirtightnessConfigurationWindow : Window
{
    private readonly AirtightnessViewModel _model;

    public AirtightnessConfigurationWindow(AirtightnessViewModel model)
    {
        if (!model.CanConfigure) throw new UnauthorizedAccessException("当前账号无权修改气密配置。");
        _model = model;
        InitializeComponent();
        var c = model.Configuration;
        MinimumPressure.Text = c.MinimumPressureKpa.ToString(CultureInfo.InvariantCulture);
        MaximumPressure.Text = c.MaximumPressureKpa.ToString(CultureInfo.InvariantCulture);
        Fill.Text = c.FillSeconds.ToString(CultureInfo.InvariantCulture);
        Balance.Text = c.BalanceSeconds.ToString(CultureInfo.InvariantCulture);
        Test.Text = c.TestSeconds.ToString(CultureInfo.InvariantCulture);
        Exhaust.Text = c.ExhaustSeconds.ToString(CultureInfo.InvariantCulture);
        MaximumLeak.Text = c.MaximumLeakPa.ToString(CultureInfo.InvariantCulture);
        TerminalResultCodes.Text = c.TerminalResultCodes;
        TerminalResultStablePolls.Text = c.TerminalResultStablePolls.ToString(CultureInfo.InvariantCulture);
        ManualStartWaitTimeout.Text = c.ManualStartWaitTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
    }

    private static decimal Parse(string text, string label) => decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
        ? value : throw new FormatException($"{label}请输入有效数字（小数点使用 .）。");
    private static int ParseInteger(string text, string label) => int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        ? value : throw new FormatException($"{label}请输入整数。");

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        AirtightnessConfiguration edited;
        try { edited = BuildEditedConfiguration(); }
        catch (Exception exception) { Error.Text = exception.Message; return; }
        if (!_model.IsInstrumentConnected)
        {
            Error.Text = "气密仪未连接。请先在“设备连接”中连接气密仪，再保存并同步工艺参数。";
            return;
        }

        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        SyncOverlay.Visibility = Visibility.Visible;
        Error.Text = string.Empty;
        try
        {
            await _model.SaveConfigurationToInstrumentAsync(edited);
            MessageBox.Show(this, "气密工艺参数已同步至仪器，并已逐项回读确认。", "同步完成", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            _model.AddLog($"气密工艺配置保存未完成：{exception.Message}", "错误");
            Error.Text = exception.Message;
        }
        finally
        {
            SyncOverlay.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private AirtightnessConfiguration BuildEditedConfiguration()
    {
        var edited = ConfigurationStore.Clone(_model.Configuration);
        edited.MinimumPressureKpa = Parse(MinimumPressure.Text, "测试气压下限");
        edited.MaximumPressureKpa = Parse(MaximumPressure.Text, "测试气压上限");
        edited.FillSeconds = Parse(Fill.Text, "充气时间");
        edited.BalanceSeconds = Parse(Balance.Text, "平衡时间");
        edited.TestSeconds = Parse(Test.Text, "测试时间");
        edited.ExhaustSeconds = Parse(Exhaust.Text, "泄气时间");
        edited.MaximumLeakPa = Parse(MaximumLeak.Text, "压降上限");
        edited.TerminalResultCodes = TerminalResultCodes.Text.Trim();
        edited.TerminalResultStablePolls = ParseInteger(TerminalResultStablePolls.Text, "连续确认次数");
        edited.ManualStartWaitTimeoutSeconds = ParseInteger(ManualStartWaitTimeout.Text, "等待面板 Start 超时");
        return edited;
    }
}