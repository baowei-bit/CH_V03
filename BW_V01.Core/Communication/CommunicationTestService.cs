using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BW_V01.Core.Configuration;

namespace BW_V01.Core.Communication;

public sealed class CommunicationTestService
{
    public async Task<CommunicationTestResult> TestAsync(DeviceConnectionConfiguration configuration, CancellationToken cancellationToken = default)
    {
        return configuration.ConnectionType switch
        {
            ConnectionType.Disabled => CommunicationTestResult.Success($"{configuration.Name} 已禁用"),
            ConnectionType.SerialPort => TestSerialPort(configuration),
            ConnectionType.TcpClient => await TestTcpClientAsync(configuration, cancellationToken),
            ConnectionType.TcpServer => TestTcpServer(configuration),
            _ => CommunicationTestResult.Fail($"不支持的通讯方式：{configuration.ConnectionType}")
        };
    }

    private static CommunicationTestResult TestSerialPort(DeviceConnectionConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.PortName))
        {
            return CommunicationTestResult.Fail($"{configuration.Name} 串口号不能为空");
        }

        if (configuration.BaudRate <= 0)
        {
            return CommunicationTestResult.Fail($"{configuration.Name} 波特率必须大于 0");
        }

        try
        {
            var assembly = Assembly.Load("System.IO.Ports");
            var serialPortType = assembly.GetType("System.IO.Ports.SerialPort");
            if (serialPortType is null)
            {
                return CommunicationTestResult.Fail("当前运行环境缺少 System.IO.Ports.SerialPort");
            }

            using var serialPort = (IDisposable?)Activator.CreateInstance(serialPortType);
            if (serialPort is null)
            {
                return CommunicationTestResult.Fail("串口对象创建失败");
            }

            SetProperty(serialPortType, serialPort, "PortName", configuration.PortName.Trim());
            SetProperty(serialPortType, serialPort, "BaudRate", configuration.BaudRate);
            SetProperty(serialPortType, serialPort, "ReadTimeout", configuration.TimeoutMilliseconds);
            SetProperty(serialPortType, serialPort, "WriteTimeout", configuration.TimeoutMilliseconds);

            serialPortType.GetMethod("Open")?.Invoke(serialPort, null);
            serialPortType.GetMethod("Close")?.Invoke(serialPort, null);

            return CommunicationTestResult.Success($"{configuration.Name} 串口连接成功：{configuration.PortName}, {configuration.BaudRate}");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return CommunicationTestResult.Fail($"{configuration.Name} 串口连接失败：{ex.InnerException.Message}");
        }
        catch (Exception ex)
        {
            return CommunicationTestResult.Fail($"{configuration.Name} 串口连接失败：{ex.Message}");
        }
    }

    private static async Task<CommunicationTestResult> TestTcpClientAsync(DeviceConnectionConfiguration configuration, CancellationToken cancellationToken)
    {
        if (!ValidateIpAndPort(configuration, out var endpoint, out var error))
        {
            return CommunicationTestResult.Fail(error);
        }

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(endpoint.Address, endpoint.Port, cancellationToken).AsTask();
            var timeoutTask = Task.Delay(configuration.TimeoutMilliseconds, cancellationToken);
            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            if (completedTask != connectTask)
            {
                return CommunicationTestResult.Fail($"{configuration.Name} TCP 连接超时：{endpoint}");
            }

            await connectTask;
            return CommunicationTestResult.Success($"{configuration.Name} TCP 客户端连接成功：{endpoint}");
        }
        catch (Exception ex)
        {
            return CommunicationTestResult.Fail($"{configuration.Name} TCP 客户端连接失败：{ex.Message}");
        }
    }

    private static CommunicationTestResult TestTcpServer(DeviceConnectionConfiguration configuration)
    {
        if (!ValidateIpAndPort(configuration, out var endpoint, out var error))
        {
            return CommunicationTestResult.Fail(error);
        }

        try
        {
            var listener = new TcpListener(endpoint);
            listener.Start();
            listener.Stop();
            return CommunicationTestResult.Success($"{configuration.Name} TCP 服务端端口可监听：{endpoint}");
        }
        catch (Exception ex)
        {
            return CommunicationTestResult.Fail($"{configuration.Name} TCP 服务端监听失败：{ex.Message}");
        }
    }

    private static bool ValidateIpAndPort(DeviceConnectionConfiguration configuration, out IPEndPoint endpoint, out string error)
    {
        endpoint = new IPEndPoint(IPAddress.Loopback, 0);
        error = string.Empty;

        if (!IPAddress.TryParse(configuration.IpAddress, out var address))
        {
            error = $"{configuration.Name} IP 地址无效：{configuration.IpAddress}";
            return false;
        }

        if (configuration.Port is <= 0 or > 65535)
        {
            error = $"{configuration.Name} 端口必须在 1 到 65535 之间";
            return false;
        }

        endpoint = new IPEndPoint(address, configuration.Port);
        return true;
    }

    private static void SetProperty(Type type, object instance, string propertyName, object value)
    {
        type.GetProperty(propertyName)?.SetValue(instance, value);
    }
}

public sealed record CommunicationTestResult(bool IsSuccess, string Message)
{
    public static CommunicationTestResult Success(string message) => new(true, message);

    public static CommunicationTestResult Fail(string message) => new(false, message);
}
