using System.IO.Ports;

namespace CH_V03.Core.Airtightness;

/// <summary>中盛等标准 Modbus RTU 数字量模块的继电器输出客户端。</summary>
public sealed class DigitalIoModbusRtuClient : IAsyncDisposable
{
    private readonly AirtightnessMotionConfiguration _configuration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SerialPort? _port;

    public DigitalIoModbusRtuClient(AirtightnessMotionConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.Validate();
    }

    public bool IsConnected => _port?.IsOpen == true;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsConnected) return Task.CompletedTask;
        _port = new SerialPort(_configuration.PortName.Trim(), _configuration.BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = _configuration.TimeoutMilliseconds, WriteTimeout = _configuration.TimeoutMilliseconds, Handshake = Handshake.None
        };
        _port.Open();
        return Task.CompletedTask;
    }

    public async Task<bool> ReadOutputAsync(int outputNumber, CancellationToken cancellationToken = default)
    {
        ValidateOutput(outputNumber);
        var address = (ushort)(outputNumber - 1);
        var response = await ExchangeAsync(BuildRequest(0x01, address, 1, null), 6, cancellationToken);
        if (response[1] != 0x01 || response[2] != 1) throw new InvalidDataException("动作控制模块读取输出返回格式无效。");
        return (response[3] & 0x01) != 0;
    }

    public async Task SetOutputAsync(int outputNumber, bool enabled, CancellationToken cancellationToken = default)
    {
        ValidateOutput(outputNumber);
        var address = (ushort)(outputNumber - 1);
        var value = enabled ? (ushort)0xFF00 : (ushort)0;
        var request = BuildRequest(0x05, address, value, null);
        var response = await ExchangeAsync(request, 8, cancellationToken);
        if (!response.AsSpan(0, 6).SequenceEqual(request.AsSpan(0, 6))) throw new InvalidDataException("动作控制模块未确认继电器输出命令。");
    }

    public Task DisconnectAsync()
    {
        var port = _port; _port = null;
        if (port is not null) { if (port.IsOpen) port.Close(); port.Dispose(); }
        return Task.CompletedTask;
    }

    private async Task<byte[]> ExchangeAsync(byte[] request, int responseLength, CancellationToken cancellationToken)
    {
        if (_port is null || !_port.IsOpen) throw new InvalidOperationException("动作控制模块未连接。");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _port.DiscardInBuffer();
            await _port.BaseStream.WriteAsync(request, cancellationToken);
            await _port.BaseStream.FlushAsync(cancellationToken);
            var response = new byte[responseLength]; var read = 0;
            using var timeout = new CancellationTokenSource(_configuration.TimeoutMilliseconds);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            while (read < response.Length)
            {
                var count = await _port.BaseStream.ReadAsync(response.AsMemory(read), linked.Token);
                if (count == 0) throw new EndOfStreamException("动作控制模块返回数据不完整。");
                read += count;
            }
            ValidateCrc(response);
            if (response[0] != _configuration.StationAddress) throw new InvalidDataException("动作控制模块返回站号不匹配。");
            if ((response[1] & 0x80) != 0) throw new InvalidDataException($"动作控制模块返回 Modbus 异常码 0x{response[2]:X2}。");
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException($"等待动作控制模块响应超时（{_configuration.TimeoutMilliseconds} ms）。"); }
        finally { _gate.Release(); }
    }

    private byte[] BuildRequest(byte function, ushort address, ushort value, byte[]? unused)
    {
        var bytes = new List<byte> { _configuration.StationAddress, function, (byte)(address >> 8), (byte)address, (byte)(value >> 8), (byte)value };
        AddCrc(bytes); return bytes.ToArray();
    }
    private static void ValidateOutput(int value) { if (value is < 1 or > 72) throw new ArgumentOutOfRangeException(nameof(value), "输出通道必须为 1–72。"); }
    private static void AddCrc(List<byte> bytes) { var crc = Crc16(bytes.ToArray()); bytes.Add((byte)crc); bytes.Add((byte)(crc >> 8)); }
    private static void ValidateCrc(byte[] bytes) { var actual = (ushort)(bytes[^2] | bytes[^1] << 8); if (actual != Crc16(bytes.AsSpan(0, bytes.Length - 2))) throw new InvalidDataException("动作控制模块返回 CRC 校验失败。"); }
    private static ushort Crc16(ReadOnlySpan<byte> bytes) { ushort crc = 0xFFFF; foreach (var value in bytes) { crc ^= value; for (var i = 0; i < 8; i++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1); } return crc; }
    public async ValueTask DisposeAsync() { await DisconnectAsync(); _gate.Dispose(); }
}
