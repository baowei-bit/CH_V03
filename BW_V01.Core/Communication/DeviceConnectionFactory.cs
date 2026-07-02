using BW_V01.Core.Configuration;

namespace BW_V01.Core.Communication;

public sealed class DeviceConnectionFactory
{
    public IDeviceConnection Create(DeviceConnectionConfiguration configuration)
    {
        return configuration.ConnectionType switch
        {
            ConnectionType.SerialPort => new SerialDeviceConnection(configuration),
            ConnectionType.TcpClient => new TcpClientDeviceConnection(configuration),
            ConnectionType.TcpServer => new TcpServerDeviceConnection(configuration),
            ConnectionType.Disabled => new DisabledDeviceConnection(configuration.Name),
            _ => throw new NotSupportedException($"不支持的通讯方式：{configuration.ConnectionType}")
        };
    }
}
