using CH_V03.Core.Communication;
using CH_V03.Core.Configuration;

namespace CH_V03.App.Communication;

internal sealed class CameraQrDeviceConnection : IDeviceConnection
{
    private readonly EndoscopeScannerService _service;

    public CameraQrDeviceConnection(DeviceConnectionConfiguration configuration, EndoscopeScannerService service)
    {
        Name = string.IsNullOrWhiteSpace(configuration.Name) ? "内窥镜二维码识别" : configuration.Name;
        _service = service;
        _service.Configure(configuration);
        _service.BarcodeDetected += Service_BarcodeDetected;
    }

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;
    public string Name { get; }
    public bool IsOpen => _service.IsOpen;

    public Task OpenAsync(CancellationToken cancellationToken = default) => _service.OpenAsync(cancellationToken);
    public Task CloseAsync(CancellationToken cancellationToken = default) => _service.CloseAsync(cancellationToken);
    public Task SendStringAsync(string data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Serial scanner commands (including stop) must never start another scan.
        return string.Equals(data.Trim(), "scan", StringComparison.OrdinalIgnoreCase)
            ? _service.TriggerScanAsync(cancellationToken)
            : Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _service.BarcodeDetected -= Service_BarcodeDetected;
        await _service.CloseAsync();
    }

    private void Service_BarcodeDetected(object? sender, string barcode) =>
        DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs(barcode));
}
