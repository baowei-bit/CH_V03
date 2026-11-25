using System.Diagnostics;
using System.Reflection;
using System.Text;
using CH_V03.Core.Configuration;

namespace CH_V03.Core.Communication;

internal sealed class SerialDeviceConnection : IDeviceConnection, IDeviceConnectionDiagnostics
{
    private const int ReliableReadBufferSize = 64 * 1024;

    private readonly DeviceConnectionConfiguration _configuration;
    private readonly Encoding _encoding = Encoding.ASCII;
    private readonly object _readGate = new();
    private object? _serialPort;
    private Type? _serialPortType;
    private Delegate? _dataReceivedHandler;

    public SerialDeviceConnection(DeviceConnectionConfiguration configuration)
    {
        _configuration = configuration;
        Name = configuration.Name;
    }

    public event EventHandler<DeviceDataReceivedEventArgs>? DataReceived;

    public event EventHandler<DeviceCommunicationErrorEventArgs>? CommunicationError;

    public string Name { get; }

    public bool IsOpen => _serialPort is not null && _serialPortType?.GetProperty("IsOpen")?.GetValue(_serialPort) is true;

    public Task OpenAsync(CancellationToken cancellationToken = default)
    {
        if (IsOpen)
        {
            return Task.CompletedTask;
        }

        var assembly = Assembly.Load("System.IO.Ports");
        _serialPortType = assembly.GetType("System.IO.Ports.SerialPort")
            ?? throw new InvalidOperationException("当前运行环境缺少 System.IO.Ports.SerialPort");

        _serialPort = Activator.CreateInstance(_serialPortType)
            ?? throw new InvalidOperationException("串口对象创建失败");

        SetProperty("PortName", _configuration.PortName.Trim());
        SetProperty("BaudRate", _configuration.BaudRate);
        SetProperty("ReadTimeout", _configuration.TimeoutMilliseconds);
        SetProperty("WriteTimeout", _configuration.TimeoutMilliseconds);
        SetProperty("ReadBufferSize", ReliableReadBufferSize);

        var eventInfo = _serialPortType.GetEvent("DataReceived")
            ?? throw new InvalidOperationException("串口 DataReceived 事件不存在");
        var methodInfo = GetType().GetMethod(nameof(OnSerialDataReceived), BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("串口事件处理方法不存在");
        _dataReceivedHandler = Delegate.CreateDelegate(eventInfo.EventHandlerType!, this, methodInfo);
        eventInfo.AddEventHandler(_serialPort, _dataReceivedHandler);

        _serialPortType.GetMethod("Open")?.Invoke(_serialPort, null);
        return Task.CompletedTask;
    }

    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        lock (_readGate)
        {
            if (_serialPort is null || _serialPortType is null)
            {
                return Task.CompletedTask;
            }

            try
            {
                var eventInfo = _serialPortType.GetEvent("DataReceived");
                if (eventInfo is not null && _dataReceivedHandler is not null)
                {
                    eventInfo.RemoveEventHandler(_serialPort, _dataReceivedHandler);
                }

                if (IsOpen)
                {
                    _serialPortType.GetMethod("Close")?.Invoke(_serialPort, null);
                }
            }
            finally
            {
                (_serialPort as IDisposable)?.Dispose();
                _serialPort = null;
                _serialPortType = null;
                _dataReceivedHandler = null;
            }
        }

        return Task.CompletedTask;
    }

    public Task SendStringAsync(string data, CancellationToken cancellationToken = default)
    {
        if (!IsOpen || _serialPort is null || _serialPortType is null)
        {
            return Task.CompletedTask;
        }

        _serialPortType.GetMethod("Write", new[] { typeof(string) })?.Invoke(_serialPort, new object[] { data });
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        DataReceived = null;
        CommunicationError = null;
    }

    private void OnSerialDataReceived(object sender, EventArgs e)
    {
        lock (_readGate)
        {
            if (_serialPort is null || _serialPortType is null)
            {
                return;
            }

            try
            {
                var bytesToReadProperty = _serialPortType.GetProperty("BytesToRead");
                var readMethod = _serialPortType.GetMethod("Read", new[] { typeof(byte[]), typeof(int), typeof(int) });
                if (bytesToReadProperty is null || readMethod is null)
                {
                    RaiseCommunicationError("串口读取失败：当前运行环境不支持必要的读取接口。");
                    return;
                }

                while (true)
                {
                    var bytesToRead = bytesToReadProperty.GetValue(_serialPort) as int? ?? 0;
                    if (bytesToRead <= 0)
                    {
                        return;
                    }

                    var buffer = new byte[bytesToRead];
                    var result = readMethod.Invoke(_serialPort, new object[] { buffer, 0, bytesToRead });
                    var bytesRead = result is int count ? count : 0;
                    if (bytesRead <= 0)
                    {
                        RaiseCommunicationError($"串口读取未返回有效字节：缓冲区待读取 {bytesToRead} 字节。");
                        return;
                    }

                    RaiseReceived(_encoding.GetString(buffer, 0, bytesRead));
                }
            }
            catch (Exception exception)
            {
                var message = exception.GetBaseException().Message;
                Trace.TraceError($"串口读取异常：{message}");
                RaiseCommunicationError($"串口读取异常：{message}");
            }
        }
    }

    private void SetProperty(string propertyName, object value)
    {
        _serialPortType?.GetProperty(propertyName)?.SetValue(_serialPort, value);
    }

    private void RaiseReceived(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            DataReceived?.Invoke(this, new DeviceDataReceivedEventArgs(text));
        }
    }

    private void RaiseCommunicationError(string message)
    {
        CommunicationError?.Invoke(this, new DeviceCommunicationErrorEventArgs(message));
    }
}
