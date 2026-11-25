using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using CH_V03.App.Airtightness;
using CH_V03.Core.Airtightness;
using CH_V03.Core.Configuration;

namespace CH_V03.App.Views;

public partial class AirtightnessBasicConfigurationWindow : Window
{
    private readonly AirtightnessViewModel _model;

    public AirtightnessBasicConfigurationWindow(AirtightnessViewModel model)
    {
        if (!model.CanConfigure) throw new UnauthorizedAccessException("当前账号无权修改基础配置。");
        _model = model;
        InitializeComponent();
        var configuration = model.Configuration;
        Product.Text = configuration.ProductName;
        CountingMode.SelectedIndex = (int)configuration.CountingMode;
        ScanMode.SelectedIndex = (int)configuration.ScanMode;
        RelayControlEnabled.IsChecked = configuration.Motion.Enabled;
        SkipRelayControl.IsChecked = !configuration.Motion.Enabled;
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var edited = ConfigurationStore.Clone(_model.Configuration);
            edited.ProductName = Product.Text.Trim();
            edited.CountingMode = (AirtightnessCountingMode)CountingMode.SelectedIndex;
            edited.ScanMode = (AirtightnessScanMode)ScanMode.SelectedIndex;
            edited.RecordsDirectory = RecordsDirectory.Text.Trim();
            edited.LogDirectory = LogDirectory.Text.Trim();
            edited.Motion.Enabled = RelayControlEnabled.IsChecked == true;
            edited.Motion.SkipRelayControlForManualTest = SkipRelayControl.IsChecked == true;
            _model.SaveBasicConfiguration(edited);
            MessageBox.Show(this, "基础配置已保存。未向气密仪下发参数。", "保存完成", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception exception) { Error.Text = exception.Message; }
    }
}