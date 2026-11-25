using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CH_V03.App.Communication;
using CH_V03.Core.Configuration;
using Windows.Devices.Enumeration;

namespace CH_V03.App.Views;

public partial class EndoscopeWindow : Window
{
    private readonly EndoscopeScannerService _service;
    private readonly DeviceConnectionConfiguration _configuration;
    private Point? _roiDragStart;
    private int _previewUpdatePending;

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
        NoReadDirectoryBox.Text = string.IsNullOrWhiteSpace(_configuration.CameraNoReadImageDirectory)
            ? EndoscopeScannerService.DefaultNoReadImageDirectory
            : _configuration.CameraNoReadImageDirectory;
        await LoadCameraChoicesAsync();
        _service.FrameReceived += Service_FrameReceived;
        _service.BarcodeDetected += Service_BarcodeDetected;
        _service.StatusChanged += Service_StatusChanged;
        await ApplyAndOpenAsync();
        UpdateRoiOverlay();
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
            QualityHintText.Text = result is null ? "请检查码区清晰度、补光、反光和是否完整入镜。" : string.Empty;
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
            QualityHintText.Text = result is null
                ? $"{_service.LastQualityAdvice} {(string.IsNullOrEmpty(_service.LastNoReadFramePath) ? "原始帧保存失败：" + _service.LastNoReadSaveError : "原始帧：" + _service.LastNoReadFramePath)}"
                : string.Empty;
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
        string directory;
        try { directory = System.IO.Path.GetFullPath(NoReadDirectoryBox.Text.Trim()); }
        catch (Exception) { MessageBox.Show(this, CH_V03.App.Localization.UiLanguage.Text("请选择有效的 NoRead 原始帧保存目录。"), CH_V03.App.Localization.UiLanguage.Text("路径无效"), MessageBoxButton.OK, MessageBoxImage.Warning); NoReadDirectoryBox.Focus(); return false; }
        _configuration.CameraIndex = cameraIndex; _configuration.CameraWidth = width; _configuration.CameraHeight = height; _configuration.CameraFrameRate = fps; _configuration.CameraScanTimeoutMilliseconds = timeout;
        _configuration.CameraNoReadImageDirectory = directory;
        _configuration.CameraAutoFocus = false;
        return true;
    }

    private void BrowseNoReadDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择 NoRead 原始帧保存目录"
        };
        if (dialog.ShowDialog(this) == true) NoReadDirectoryBox.Text = dialog.FolderName;
    }

    private void ClearRoiButton_Click(object sender, RoutedEventArgs e)
    {
        _configuration.CameraRoiX = _configuration.CameraRoiY = 0;
        _configuration.CameraRoiWidth = _configuration.CameraRoiHeight = 0;
        UpdateRoiOverlay();
        QualityHintText.Text = "识别区域已清除；保存后正式扫码使用整幅画面。";
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateRoiOverlay();

    private void PreviewHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!TryGetImageBounds(out var bounds)) return;
        var point = e.GetPosition(PreviewHost);
        if (!bounds.Contains(point)) return;
        _roiDragStart = point;
        PreviewHost.CaptureMouse();
        ShowSelection(new Rect(point, point));
        e.Handled = true;
    }

    private void PreviewHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (_roiDragStart is null || !TryGetImageBounds(out var bounds)) return;
        var point = ClampToBounds(e.GetPosition(PreviewHost), bounds);
        ShowSelection(new Rect(_roiDragStart.Value, point));
    }

    private void PreviewHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_roiDragStart is null) return;
        PreviewHost.ReleaseMouseCapture();
        if (TryGetImageBounds(out var bounds))
        {
            var selected = new Rect(_roiDragStart.Value, ClampToBounds(e.GetPosition(PreviewHost), bounds));
            if (selected.Width >= 20 && selected.Height >= 20)
            {
                _configuration.CameraRoiX = (selected.Left - bounds.Left) / bounds.Width;
                _configuration.CameraRoiY = (selected.Top - bounds.Top) / bounds.Height;
                _configuration.CameraRoiWidth = selected.Width / bounds.Width;
                _configuration.CameraRoiHeight = selected.Height / bounds.Height;
                QualityHintText.Text = "识别区域已选定；点击“保存并启用内窥镜扫码”后，正式扫码优先识别该区域。";
            }
        }
        _roiDragStart = null;
        UpdateRoiOverlay();
        e.Handled = true;
    }

    private bool TryGetImageBounds(out Rect bounds)
    {
        bounds = Rect.Empty;
        if (PreviewImage.Source is not BitmapSource source || PreviewHost.ActualWidth <= 0 || PreviewHost.ActualHeight <= 0) return false;
        var scale = Math.Min(PreviewHost.ActualWidth / source.PixelWidth, PreviewHost.ActualHeight / source.PixelHeight);
        var width = source.PixelWidth * scale;
        var height = source.PixelHeight * scale;
        bounds = new Rect((PreviewHost.ActualWidth - width) / 2, (PreviewHost.ActualHeight - height) / 2, width, height);
        return true;
    }

    private static Point ClampToBounds(Point point, Rect bounds) => new(
        Math.Clamp(point.X, bounds.Left, bounds.Right), Math.Clamp(point.Y, bounds.Top, bounds.Bottom));

    private void ShowSelection(Rect selected)
    {
        RoiRectangle.Visibility = Visibility.Visible;
        Canvas.SetLeft(RoiRectangle, selected.Left);
        Canvas.SetTop(RoiRectangle, selected.Top);
        RoiRectangle.Width = selected.Width;
        RoiRectangle.Height = selected.Height;
    }

    private void UpdateRoiOverlay()
    {
        if (!TryGetImageBounds(out var bounds) || _configuration.CameraRoiWidth <= 0 || _configuration.CameraRoiHeight <= 0)
        {
            RoiRectangle.Visibility = Visibility.Collapsed;
            return;
        }
        ShowSelection(new Rect(
            bounds.Left + _configuration.CameraRoiX * bounds.Width,
            bounds.Top + _configuration.CameraRoiY * bounds.Height,
            _configuration.CameraRoiWidth * bounds.Width,
            _configuration.CameraRoiHeight * bounds.Height));
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
        MessageBox.Show(this, CH_V03.App.Localization.UiLanguage.IsEnglish ? $"Enter a value from {minimum} to {maximum}." : $"请输入 {minimum} 到 {maximum} 之间的数值。", CH_V03.App.Localization.UiLanguage.Text("参数无效"), MessageBoxButton.OK, MessageBoxImage.Warning); box.Focus(); return false;
    }

    private void Service_FrameReceived(object? sender, BitmapSource image)
    {
        if (Interlocked.Exchange(ref _previewUpdatePending, 1) != 0) return;
        Dispatcher.BeginInvoke(() =>
        {
            try
            {
                if (!IsLoaded) return;
                PreviewImage.Source = image;
                PreviewHint.Visibility = Visibility.Collapsed;
                if (_roiDragStart is null) UpdateRoiOverlay();
            }
            finally { Interlocked.Exchange(ref _previewUpdatePending, 0); }
        });
    }
    private void Service_BarcodeDetected(object? sender, string barcode) => Dispatcher.BeginInvoke(() => LastResultText.Text = barcode);
    private void Service_StatusChanged(object? sender, string status) => Dispatcher.BeginInvoke(() => StatusText.Text = status);

    private sealed record CameraChoice(int Index, string Name);
}
