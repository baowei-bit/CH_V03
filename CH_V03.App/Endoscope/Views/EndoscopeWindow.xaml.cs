using System.Globalization;
using System.Windows;
using System.Windows.Media.Imaging;
using CH_V03.App.Communication;
using CH_V03.Core.Configuration;
using Windows.Devices.Enumeration;

namespace CH_V03.App.Views;

public partial class EndoscopeWindow : Window
{
    private readonly EndoscopeScannerService _service;
    private readonly DeviceConnectionConfiguration _configuration;

    internal EndoscopeWindow(EndoscopeScannerService service, DeviceConnectionConfiguration configuration)
    {
        _service = service;
        _configuration = configuration;
        InitializeComponent();
        Loaded += EndoscopeWindow_Loaded;
        Closed += EndoscopeWindow_Closed;
    }

    private async void EndoscopeWindow_Loaded(object sender, RoutedEventArgs e)
    {
        CameraIndexBox.Text = _configuration.CameraIndex.ToString(CultureInfo.InvariantCulture);
        WidthBox.Text = _configuration.CameraWidth.ToString(CultureInfo.InvariantCulture);
        HeightBox.Text = _configuration.CameraHeight.ToString(CultureInfo.InvariantCulture);
        FrameRateBox.Text = _configuration.CameraFrameRate.ToString(CultureInfo.InvariantCulture);
        ScanTimeoutBox.Text = _configuration.CameraScanTimeoutMilliseconds.ToString(CultureInfo.InvariantCulture);
        await LoadCameraChoicesAsync();
        _service.FrameReceived += Service_FrameReceived;
        _service.BarcodeDetected += Service_BarcodeDetected;
        _service.StatusChanged += Service_StatusChanged;
        await ApplyAndOpenAsync();
    }

    private void EndoscopeWindow_Closed(object? sender, EventArgs e)
    {
        _service.FrameReceived -= Service_FrameReceived;
        _service.BarcodeDetected -= Service_BarcodeDetected;
        _service.StatusChanged -= Service_StatusChanged;
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e) => await ApplyAndOpenAsync();
    private async void DecodeImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择需要识别的图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true) return;
        var button = (System.Windows.Controls.Button)sender;
        button.IsEnabled = false;
        try
        {
            StatusText.Text = "正在识别图片…";
            var result = await _service.DecodeImageFileAsync(dialog.FileName);
            LastResultText.Text = result ?? "NoRead";
            StatusText.Text = result is null ? "图片中未识别到码" : "图片识别成功（仅测试，不触发生产流程）";
        }
        catch (Exception ex) { StatusText.Text = $"图片识码失败：{ex.Message}"; }
        finally { button.IsEnabled = true; }
    }
    private void CameraDeviceBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (CameraDeviceBox.SelectedItem is CameraChoice choice)
        {
            CameraIndexBox.Text = choice.Index.ToString(CultureInfo.InvariantCulture);
        }
    }
    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender;
        button.IsEnabled = false;
        LastResultText.Text = "—";
        try
        {
            StatusText.Text = "正在识别当前抓图…";
            var result = await _service.DecodeCurrentFrameAsync();
            LastResultText.Text = result ?? "NoRead";
            StatusText.Text = result is null
                ? "本次抓图中未识别到码，可调整位置后重试"
                : "抓图识别成功（仅测试，不触发生产流程）";
        }
        catch (Exception ex) { StatusText.Text = $"抓图识码失败：{ex.Message}"; }
        finally { button.IsEnabled = true; }
    }
    private void SaveButton_Click(object sender, RoutedEventArgs e) { if (ApplySettings()) { DialogResult = true; Close(); } }
    private void CancelButton_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

    private async Task ApplyAndOpenAsync()
    {
        if (!ApplySettings()) return;
        try { _service.Configure(_configuration); await _service.RestartAsync(); }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private bool ApplySettings()
    {
        if (!TryRead(CameraIndexBox, 0, 20, out var cameraIndex) || !TryRead(WidthBox, 160, 3840, out var width) || !TryRead(HeightBox, 120, 2160, out var height) || !TryRead(FrameRateBox, 1, 120, out var fps) || !TryRead(ScanTimeoutBox, 300, 15000, out var timeout)) return false;
        _configuration.CameraIndex = cameraIndex; _configuration.CameraWidth = width; _configuration.CameraHeight = height; _configuration.CameraFrameRate = fps; _configuration.CameraScanTimeoutMilliseconds = timeout;
        return true;
    }

    private async Task LoadCameraChoicesAsync()
    {
        try
        {
            var cameras = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var choices = cameras
                .Select((camera, index) => new CameraChoice(index, camera.Name))
                .ToList();
            foreach (var choice in choices)
            {
                CameraDeviceBox.Items.Add(choice);
            }

            // Supereyes 常见的彩色画面端点名称；IR 通道不适合作为默认扫码画面。
            var preferred = choices.FirstOrDefault(choice => choice.Name.Contains("FHD UVC", StringComparison.OrdinalIgnoreCase)
                || choice.Name.Contains("Supereyes", StringComparison.OrdinalIgnoreCase));
            var selected = preferred ?? choices.FirstOrDefault(choice => choice.Index == _configuration.CameraIndex);
            if (selected is not null)
            {
                CameraDeviceBox.SelectedItem = selected;
                CameraIndexBox.Text = selected.Index.ToString(CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"无法列出视频设备：{ex.Message}";
        }
    }

    private bool TryRead(System.Windows.Controls.TextBox box, int minimum, int maximum, out int value)
    {
        if (int.TryParse(box.Text, out value) && value >= minimum && value <= maximum) return true;
        MessageBox.Show(this, $"请输入 {minimum} 到 {maximum} 之间的数值。", "参数无效", MessageBoxButton.OK, MessageBoxImage.Warning); box.Focus(); return false;
    }

    private void Service_FrameReceived(object? sender, BitmapSource image) => Dispatcher.BeginInvoke(() => { PreviewImage.Source = image; PreviewHint.Visibility = Visibility.Collapsed; });
    private void Service_BarcodeDetected(object? sender, string barcode) => Dispatcher.BeginInvoke(() => LastResultText.Text = barcode);
    private void Service_StatusChanged(object? sender, string status) => Dispatcher.BeginInvoke(() => StatusText.Text = status);

    private sealed record CameraChoice(int Index, string Name);
}
