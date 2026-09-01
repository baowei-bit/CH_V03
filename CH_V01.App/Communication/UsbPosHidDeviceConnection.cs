using CH_V01.Core.Communication;
using CH_V01.Core.Configuration;
using Windows.Devices.PointOfService;
using Windows.Storage.Streams;

namespace CH_V01.App.Communication;

internal sealed class UsbPosHidDeviceConnection : IDeviceConnection
{
    private BarcodeScanner? _scanner;
    private ClaimedBarcodeScanner? _claimedScanner;

    public UsbPosHidDeviceConnection(DeviceConnectionConfiguration configuration)
    {
        Name = configuration.Name;
    }

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;

    public string Name { get; }

    public bool IsOpen => _claimedScanner?.IsEnabled == true;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (IsOpen)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await CloseAsync(CancellationToken.None);

        BarcodeScanner? scanner = null;
        ClaimedBarcodeScanner? claimedScanner = null;

        try
        {
            scanner = await BarcodeScanner.GetDefaultAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (scanner is null)
            {
                throw new InvalidOperationException("未检测到 USB POS HID 条形码扫描器");
            }

            claimedScanner = await scanner.ClaimScannerAsync();
            cancellationToken.ThrowIfCancellationRequested();
            if (claimedScanner is null)
            {
                throw new InvalidOperationException("扫码枪正被其他程序占用，无法独占连接");
            }

            claimedScanner.DataReceived += ClaimedScanner_DataReceived;
            claimedScanner.ReleaseDeviceRequested += ClaimedScanner_ReleaseDeviceRequested;
            claimedScanner.IsDecodeDataEnabled = true;

            await claimedScanner.EnableAsync();
            if (!claimedScanner.IsEnabled)
            {
                throw new InvalidOperationException("扫码枪已识别，但启用失败");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _scanner = scanner;
            _claimedScanner = claimedScanner;
        }
        catch
        {
            if (claimedScanner is not null)
            {
                claimedScanner.DataReceived -= ClaimedScanner_DataReceived;
                claimedScanner.ReleaseDeviceRequested -= ClaimedScanner_ReleaseDeviceRequested;
                claimedScanner.Dispose();
            }

            scanner?.Dispose();
            throw;
        }
    }

    public async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        var claimedScanner = _claimedScanner;
        var scanner = _scanner;
        _claimedScanner = null;
        _scanner = null;

        if (claimedScanner is not null)
        {
            claimedScanner.DataReceived -= ClaimedScanner_DataReceived;
            claimedScanner.ReleaseDeviceRequested -= ClaimedScanner_ReleaseDeviceRequested;

            try
            {
                if (claimedScanner.IsEnabled)
                {
                    await claimedScanner.DisableAsync();
                }
            }
            finally
            {
                claimedScanner.Dispose();
            }
        }

        scanner?.Dispose();
        cancellationToken.ThrowIfCancellationRequested();
    }

    public Task SendStringAsync(string data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("USB POS HID 模式不支持发送扫码触发或停止命令，请按扫码枪按钮触发扫码");
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None);
    }

    private void ClaimedScanner_DataReceived(
        ClaimedBarcodeScanner sender,
        BarcodeScannerDataReceivedEventArgs args)
    {
        var buffer = args.Report.ScanDataLabel;
        if (buffer is null || buffer.Length == 0)
        {
            return;
        }

        using var reader = DataReader.FromBuffer(buffer);
        reader.UnicodeEncoding = UnicodeEncoding.Utf8;
        var barcode = reader.ReadString(buffer.Length);
        if (!string.IsNullOrWhiteSpace(barcode))
        {
            DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs(barcode));
        }
    }

    private void ClaimedScanner_ReleaseDeviceRequested(
        object? sender,
        ClaimedBarcodeScanner claimedScanner)
    {
        try
        {
            claimedScanner.RetainDevice();
        }
        catch
        {
            // 设备拔出或系统强制释放时，由下一次扫码或重连操作报告状态。
        }
    }
}
