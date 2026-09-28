using CH_V03.Core.Configuration;

namespace CH_V03.Core.Communication;

public sealed class DeviceConnectionFactory
{
    private readonly Func<DeviceConnectionConfiguration, IDeviceConnection>? _usbPosHidFactory;
    private readonly Func<DeviceConnectionConfiguration, IDeviceConnection>? _cameraQrFactory;

    public DeviceConnectionFactory(
        Func<DeviceConnectionConfiguration, IDeviceConnection>? usbPosHidFactory = null,
        Func<DeviceConnectionConfiguration, IDeviceConnection>? cameraQrFactory = null)
    {
        _usbPosHidFactory = usbPosHidFactory;
        _cameraQrFactory = cameraQrFactory;
    }

    public IDeviceConnection Create(DeviceConnectionConfiguration configuration)
    {
        return configuration.ConnectionType switch
        {
            ConnectionType.SerialPort => new SerialDeviceConnection(configuration),
            ConnectionType.TcpClient => new TcpClientDeviceConnection(configuration),
            ConnectionType.TcpServer => new TcpServerDeviceConnection(configuration),
            ConnectionType.Disabled => new DisabledDeviceConnection(configuration.Name),
            ConnectionType.UsbPosHid when _usbPosHidFactory is not null => _usbPosHidFactory(configuration),
            ConnectionType.UsbPosHid => throw new NotSupportedException("当前应用未注册 USB POS HID 连接提供器"),
            ConnectionType.CameraQr when _cameraQrFactory is not null => _cameraQrFactory(configuration),
            ConnectionType.CameraQr => throw new NotSupportedException("当前应用未注册内窥镜二维码连接提供器"),
            _ => throw new NotSupportedException($"不支持的通讯方式：{configuration.ConnectionType}")
        };
    }
}
