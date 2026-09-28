using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
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
        Product.Text = c.ProductName;
        CountingMode.SelectedIndex = (int)c.CountingMode;
        ScanMode.SelectedIndex = (int)c.ScanMode;
        MinimumPressure.Text = c.MinimumPressureKpa.ToString(CultureInfo.InvariantCulture);
        MaximumPressure.Text = c.MaximumPressureKpa.ToString(CultureInfo.InvariantCulture);
        Fill.Text = c.FillSeconds.ToString(CultureInfo.InvariantCulture);
        Balance.Text = c.BalanceSeconds.ToString(CultureInfo.InvariantCulture);
        Test.Text = c.TestSeconds.ToString(CultureInfo.InvariantCulture);
        Exhaust.Text = c.ExhaustSeconds.ToString(CultureInfo.InvariantCulture);
        MaximumLeak.Text = c.MaximumLeakPa.ToString(CultureInfo.InvariantCulture);
        RecordsDirectory.Text = model.RecordsDirectory;
        LogDirectory.Text = model.LogDirectory;
    }

    private void SelectRecordsDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(RecordsDirectory);
    private void SelectLogDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(LogDirectory);

    private void SelectDirectory(TextBox target)
    {
        var dialog = new OpenFolderDialog { Title = "选择保存文件夹" };
        if (Directory.Exists(target.Text.Trim())) dialog.InitialDirectory = target.Text.Trim();
        if (dialog.ShowDialog(this) == true) target.Text = dialog.FolderName;
    }

    private static decimal Parse(string text, string label) =>
        decimal.TryParse(text.Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? value : throw new FormatException($"{label}请输入有效数字（小数点使用 .）。");

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_model.IsInstrumentConnected)
        {
            Error.Text = "气密仪未连接。请先关闭本窗口，在主界面点击“气密仪”连接成功后再保存配置。";
            Success.Text = string.Empty;
            return;
        }

        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        SyncOverlay.Visibility = Visibility.Visible;
        Error.Text = string.Empty;
        Success.Text = string.Empty;
        try
        {
            var edited = ConfigurationStore.Clone(_model.Configuration);
            edited.ProductName = Product.Text.Trim();
            edited.CountingMode = (AirtightnessCountingMode)CountingMode.SelectedIndex;
            edited.ScanMode = (AirtightnessScanMode)ScanMode.SelectedIndex;
            edited.MinimumPressureKpa = Parse(MinimumPressure.Text, "测试气压下限");
            edited.MaximumPressureKpa = Parse(MaximumPressure.Text, "测试气压上限");
            edited.FillSeconds = Parse(Fill.Text, "充气时间");
            edited.BalanceSeconds = Parse(Balance.Text, "平衡时间");
            edited.TestSeconds = Parse(Test.Text, "测试时间");
            edited.ExhaustSeconds = Parse(Exhaust.Text, "泄气时间");
            edited.MaximumLeakPa = Parse(MaximumLeak.Text, "压降上限");
            edited.RecordsDirectory = RecordsDirectory.Text.Trim();
            edited.LogDirectory = LogDirectory.Text.Trim();
            await _model.SaveConfigurationToInstrumentAsync(edited);
            Success.Text = "配置已同步至气密仪，并已逐项回读确认。";
            MessageBox.Show(this, Success.Text, "同步完成", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            _model.AddLog($"气密配置保存未完成：{exception.Message}", "错误");
            Error.Text = exception.Message;
        }
        finally
        {
            SyncOverlay.Visibility = Visibility.Collapsed;
            SaveButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }
}
