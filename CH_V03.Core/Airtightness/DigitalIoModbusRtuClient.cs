using System.IO.Ports;
using System.Diagnostics;

namespace CH_V03.Core.Airtightness;

/// <summary>中盛数字量模块：04 读输入、06 写普通模式输出。</summary>
public sealed class DigitalIoModbusRtuClient : IAsyncDisposable
{
    private readonly AirtightnessMotionConfiguration _configuration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Queue<string> _recentExchanges = new();
    private SerialPort? _port;
    private long _exchangeNumber;

    public DigitalIoModbusRtuClient(AirtightnessMotionConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.Validate();
    }

    public bool IsConnected => _port?.IsOpen == true;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { ConnectCore(cancellationToken); }
        finally { _gate.Release(); }
    }

    private void ConnectCore(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsConnected) return;
        _port = new SerialPort(_configuration.PortName.Trim(), _configuration.BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = _configuration.TimeoutMilliseconds, WriteTimeout = _configuration.TimeoutMilliseconds, Handshake = Handshake.None
        };
        _port.Open();
    }

    /// <summary>04 读取输入寄存器。X1 对应厂家网页中的地址 0000H。</summary>
    public async Task<bool> ReadInputAsync(int inputNumber, CancellationToken cancellationToken = default)
    {
        ValidateInput(inputNumber);
        var address = (ushort)(inputNumber - 1);
        var response = await ExchangeAsync(BuildRequest(0x04, address, 1), 7, cancellationToken);
        if (response[1] != 0x04 || response[2] != 2) throw new InvalidDataException("动作控制模块读取输入返回格式无效。");
        var value = (ushort)(response[3] << 8 | response[4]);
        if (value is not (0 or 1)) throw new InvalidDataException($"动作控制模块 X{inputNumber} 返回状态值 {value} 无效。");
        return value == 1;
    }

    public async Task SetOutputAsync(int outputNumber, bool enabled, CancellationToken cancellationToken = default)
    {
        ValidateOutput(outputNumber);
        var address = (ushort)(outputNumber - 1);
        // 厂家网页规定普通模式以保持寄存器 0001/0000 控制输出。
        var value = enabled ? (ushort)1 : (ushort)0;
        var request = BuildRequest(0x06, address, value);
        var response = await ExchangeAsync(request, 8, cancellationToken);
        if (!response.AsSpan().SequenceEqual(request)) throw new InvalidDataException("动作控制模块未确认继电器输出命令。");
    }

    public async Task DisconnectAsync()
    {
        await _gate.WaitAsync();
        try { DisconnectCore(); }
        finally { _gate.Release(); }
    }

    /// <summary>After a failed exchange, clear late bytes by reopening only the motion module port.</summary>
    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            DisconnectCore();
            await Task.Delay(300, cancellationToken);
            ConnectCore(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private void DisconnectCore()
    {
        var port = _port; _port = null;
        if (port is not null) { if (port.IsOpen) port.Close(); port.Dispose(); }
    }

    private async Task<byte[]> ExchangeAsync(byte[] request, int responseLength, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        var exchangeNumber = ++_exchangeNumber;
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var receivedChunks = new List<string>();
        var response = new byte[responseLength];
        var received = 0;
        var pendingBeforeSend = 0;
        try
        {
            var port = _port;
            if (port is null || !port.IsOpen) throw new InvalidOperationException("动作控制模块未连接。");
            pendingBeforeSend = port.BytesToRead;
            port.DiscardInBuffer();
            await port.BaseStream.WriteAsync(request, cancellationToken);
            await port.BaseStream.FlushAsync(cancellationToken);
            using var timeout = new CancellationTokenSource(_configuration.TimeoutMilliseconds);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            while (received < response.Length)
            {
                var count = await port.BaseStream.ReadAsync(response.AsMemory(received), linked.Token);
                if (count == 0) throw new EndOfStreamException("动作控制模块返回数据不完整。");
                receivedChunks.Add($"+{stopwatch.ElapsedMilliseconds} ms [{SpanToHex(response.AsSpan(received, count))}]");
                received += count;
            }
            ValidateCrc(request, response);
            if (response[0] != _configuration.StationAddress) throw new InvalidDataException("动作控制模块返回站号不匹配。");
            if ((response[1] & 0x80) != 0) throw new InvalidDataException($"动作控制模块返回 Modbus 异常码 0x{response[2]:X2}。");
            if (response[1] != request[1]) throw new InvalidDataException($"动作控制模块返回功能码不匹配：请求 {request[1]:X2}，收到 {response[1]:X2}。");
            if (request[1] == 0x06 && !response.AsSpan().SequenceEqual(request))
                throw new InvalidDataException("动作控制模块未确认继电器输出命令。");
            RememberExchange($"#{exchangeNumber} {startedAt:HH:mm:ss.fff} {ToHex(request)} -> {ToHex(response)} ({stopwatch.ElapsedMilliseconds} ms)");
            return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"等待动作控制模块响应超时（{_configuration.TimeoutMilliseconds} ms）。" +
                BuildExchangeDiagnostic(exchangeNumber, startedAt, stopwatch.ElapsedMilliseconds, request, responseLength,
                    response.AsSpan(0, received), pendingBeforeSend, receivedChunks));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException(exception.Message +
                BuildExchangeDiagnostic(exchangeNumber, startedAt, stopwatch.ElapsedMilliseconds, request, responseLength,
                    response.AsSpan(0, received), pendingBeforeSend, receivedChunks), exception);
        }
        finally { _gate.Release(); }
    }

    private void RememberExchange(string exchange)
    {
        _recentExchanges.Enqueue(exchange);
        while (_recentExchanges.Count > 4) _recentExchanges.Dequeue();
    }

    private string BuildExchangeDiagnostic(long number, DateTimeOffset startedAt, long elapsedMilliseconds,
        byte[] request, int responseLength, ReadOnlySpan<byte> response, int pendingBeforeSend, List<string> receivedChunks)
    {
        var prefix = FindValidFramePrefix(response);
        var frameHint = prefix > 0
            ? $"；接收前 {prefix} 字节可单独构成 CRC 正确的 Modbus 帧，后续 {response.Length - prefix} 字节: {SpanToHex(response[prefix..])}"
            : string.Empty;
        var recent = _recentExchanges.Count == 0 ? "无" : string.Join(" | ", _recentExchanges);
        return $" 通讯追踪：{_configuration.PortName}/{_configuration.BaudRate}/8N1/站号{_configuration.StationAddress}；" +
            $"#{number} {startedAt:HH:mm:ss.fff}，耗时 {elapsedMilliseconds} ms；发送前缓冲区 {pendingBeforeSend} 字节；" +
            $"发送 {ToHex(request)}；已收 {response.Length}/{responseLength} 字节: {SpanToHex(response)}；" +
            $"分段 {string.Join(" | ", receivedChunks)}{frameHint}；前 4 次成功交换: {recent}。";
    }

    private static int FindValidFramePrefix(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 6) return 0;
        var function = bytes[1];
        var length = function is >= 0x01 and <= 0x04 ? 3 + bytes[2] + 2 : function is 0x05 or 0x06 ? 8 : 0;
        if (length < 5 || length >= bytes.Length) return 0;
        var crc = Crc16(bytes[..(length - 2)]);
        return bytes[length - 2] == (byte)crc && bytes[length - 1] == (byte)(crc >> 8) ? length : 0;
    }

    private byte[] BuildRequest(byte function, ushort address, ushort value)
    {
        var bytes = new List<byte> { _configuration.StationAddress, function, (byte)(address >> 8), (byte)address, (byte)(value >> 8), (byte)value };
        AddCrc(bytes); return bytes.ToArray();
    }
    private static void ValidateOutput(int value) { if (value is < 1 or > 48) throw new ArgumentOutOfRangeException(nameof(value), "厂家新协议列出的输出通道为 1–48。"); }
    private static void ValidateInput(int value) { if (value is < 1 or > 48) throw new ArgumentOutOfRangeException(nameof(value), "厂家新协议列出的输入通道为 1–48。"); }
    private static void AddCrc(List<byte> bytes) { var crc = Crc16(bytes.ToArray()); bytes.Add((byte)crc); bytes.Add((byte)(crc >> 8)); }
    private static void ValidateCrc(byte[] request, byte[] response)
    {
        var actual = (ushort)(response[^2] | response[^1] << 8);
        var expected = Crc16(response.AsSpan(0, response.Length - 2));
        if (actual == expected) return;

        throw new InvalidDataException(
            $"动作控制模块返回 CRC 校验失败。发送: {ToHex(request)}；接收: {ToHex(response)}；" +
            $"CRC 期望（低字节在前）: {(byte)expected:X2} {(byte)(expected >> 8):X2} (0x{expected:X4})；" +
            $"实际: {(byte)actual:X2} {(byte)(actual >> 8):X2} (0x{actual:X4})。");
    }
    private static string ToHex(IEnumerable<byte> bytes) => string.Join(' ', bytes.Select(value => value.ToString("X2")));
    private static string SpanToHex(ReadOnlySpan<byte> bytes) => string.Join(' ', bytes.ToArray().Select(value => value.ToString("X2")));
    private static ushort Crc16(ReadOnlySpan<byte> bytes) { ushort crc = 0xFFFF; foreach (var value in bytes) { crc ^= value; for (var i = 0; i < 8; i++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1); } return crc; }
    public async ValueTask DisposeAsync() { await DisconnectAsync(); _gate.Dispose(); }
}
