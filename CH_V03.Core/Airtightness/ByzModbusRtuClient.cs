using System.IO.Ports;

namespace CH_V03.Core.Airtightness;

/// <summary>
/// 博仪 BYZ 系列的最小 Modbus RTU 客户端。
/// 写入配方后由调用方回读核验，避免上位机显示值与仪器实际值不一致。
/// </summary>
public sealed class ByzModbusRtuClient : IAsyncDisposable
{
    private readonly AirtightnessInstrumentConfiguration _configuration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SerialPort? _port;

    public ByzModbusRtuClient(AirtightnessInstrumentConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _configuration.Validate();
    }

    public bool IsConnected => _port?.IsOpen == true;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsConnected) return Task.CompletedTask;
        _port?.Dispose();
        _port = new SerialPort(_configuration.PortName.Trim(), _configuration.BaudRate, Parity.None, 8, StopBits.One)
        {
            ReadTimeout = _configuration.TimeoutMilliseconds,
            WriteTimeout = _configuration.TimeoutMilliseconds,
            Handshake = Handshake.None
        };
        _port.Open();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        var port = _port;
        if (port is not null) AbortPort(port);
        return Task.CompletedTask;
    }

    /// <summary>中断正在等待的串口响应。用于人工停止或无响应时，使异步读不会无限挂起。</summary>
    public void AbortPendingExchange()
    {
        var port = _port;
        if (port is not null) AbortPort(port);
    }

    /// <summary>在不支持某个功能码并超时后，重新建立串口会话以继续读取其它寄存器。</summary>
    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await DisconnectAsync();
        await ConnectAsync(cancellationToken);
    }

    public async Task WriteRecipeAsync(AirtightnessConfiguration recipe, CancellationToken cancellationToken = default)
    {
        await WriteChannelRecipeAsync(1, recipe, cancellationToken);
        await WriteChannelRecipeAsync(2, recipe, cancellationToken);
    }

    public async Task VerifyRecipeAsync(AirtightnessConfiguration recipe, CancellationToken cancellationToken = default)
    {
        await VerifyChannelRecipeAsync(1, recipe, cancellationToken);
        await VerifyChannelRecipeAsync(2, recipe, cancellationToken);
    }

    public async Task<ByzChannelStageStatus> ReadChannelStageStatusAsync(int channel, CancellationToken cancellationToken = default)
    {
        var address = channel switch
        {
            1 => (ushort)6021,
            2 => (ushort)6051,
            _ => throw new ArgumentOutOfRangeException(nameof(channel), "通道必须是 1 或 2。")
        };
        var bits = await ReadInputBitsAsync(address, 5, cancellationToken);
        return new ByzChannelStageStatus(channel, bits[0], bits[1], bits[2], bits[3], bits[4]);
    }

    public async Task<ByzChannelMeasurement> ReadChannelMeasurementAsync(int channel, CancellationToken cancellationToken = default)
    {
        var pressure = await ReadChannelPressureAsync(channel, cancellationToken);
        var leak = await ReadFloatAsync(ChannelAddress(channel, 350), cancellationToken);
        var resultCode = await ReadUInt16Async(ChannelAddress(channel, 11), cancellationToken);
        return new ByzChannelMeasurement(channel, pressure, leak, resultCode);
    }

    /// <summary>读取已由现场验证可用的实时压力寄存器；不读取压降或结果码，以免未知地址阻断波形采集。</summary>
    public Task<float> ReadChannelPressureAsync(int channel, CancellationToken cancellationToken = default) =>
        ReadFloatAsync(ChannelAddress(channel, 30), cancellationToken);

    public async Task<ByzChannelMeasurement> ReadChannelFinalMeasurementAsync(int channel, CancellationToken cancellationToken = default)
    {
        var pressure = await ReadFloatAsync(ChannelAddress(channel, 265), cancellationToken);
        var leak = await ReadFloatAsync(ChannelAddress(channel, 269), cancellationToken);
        // 厂家双通道协议将最终检测结果定义在测试页：CH1=11，CH2=1011（CH1 地址 +1000）。数据记录页 272/1272 返回的是空字符串，不能作为结果码。
        var resultCode = await ReadUInt16Async(ChannelAddress(channel, 11), cancellationToken);
        return new ByzChannelMeasurement(channel, pressure, leak, resultCode);
    }

    private async Task WriteChannelRecipeAsync(int channel, AirtightnessConfiguration recipe, CancellationToken cancellationToken)
    {
        await WriteUInt16Async(ChannelAddress(channel, 101), TimeToRegister(recipe.FillSeconds, "充气时间"), cancellationToken);
        await WriteUInt16Async(ChannelAddress(channel, 102), TimeToRegister(recipe.BalanceSeconds, "平衡时间"), cancellationToken);
        await WriteUInt16Async(ChannelAddress(channel, 103), TimeToRegister(recipe.TestSeconds, "检测时间"), cancellationToken);
        await WriteUInt16Async(ChannelAddress(channel, 104), TimeToRegister(recipe.ExhaustSeconds, "排气时间"), cancellationToken);
        await WriteFloatAsync(ChannelAddress(channel, 106), (float)recipe.MinimumPressureKpa, cancellationToken);
        await WriteFloatAsync(ChannelAddress(channel, 108), (float)recipe.MaximumPressureKpa, cancellationToken);
        await WriteFloatAsync(ChannelAddress(channel, 112), (float)recipe.MaximumLeakPa, cancellationToken);
    }

    private async Task VerifyChannelRecipeAsync(int channel, AirtightnessConfiguration recipe, CancellationToken cancellationToken)
    {
        var prefix = $"CH{channel}";
        VerifyTime(recipe.FillSeconds, await ReadUInt16Async(ChannelAddress(channel, 101), cancellationToken), $"{prefix}充气时间");
        VerifyTime(recipe.BalanceSeconds, await ReadUInt16Async(ChannelAddress(channel, 102), cancellationToken), $"{prefix}平衡时间");
        VerifyTime(recipe.TestSeconds, await ReadUInt16Async(ChannelAddress(channel, 103), cancellationToken), $"{prefix}检测时间");
        VerifyTime(recipe.ExhaustSeconds, await ReadUInt16Async(ChannelAddress(channel, 104), cancellationToken), $"{prefix}排气时间");
        Verify((float)recipe.MinimumPressureKpa, await ReadFloatAsync(ChannelAddress(channel, 106), cancellationToken), $"{prefix}进气下限");
        Verify((float)recipe.MaximumPressureKpa, await ReadFloatAsync(ChannelAddress(channel, 108), cancellationToken), $"{prefix}进气上限");
        Verify((float)recipe.MaximumLeakPa, await ReadFloatAsync(ChannelAddress(channel, 112), cancellationToken), $"{prefix}泄漏量上限");
    }

    private static ushort ChannelAddress(int channel, int channel1Address)
    {
        if (channel is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(channel), "通道必须是 1 或 2。");
        return checked((ushort)(channel == 1 ? channel1Address : channel1Address + 1000));
    }

    public async Task WriteUInt16Async(ushort address, ushort value, CancellationToken cancellationToken = default)
    {
        var response = await ExchangeAsync(BuildWriteSingle(address, value), 8, cancellationToken);
        if (!response.AsSpan(0, 6).SequenceEqual(BuildWriteSingle(address, value).AsSpan(0, 6)))
            throw new InvalidDataException("气密仪未确认参数写入。");
    }

    public async Task WriteCoilAsync(ushort address, bool value, CancellationToken cancellationToken = default)
    {
        var request = BuildWriteCoil(address, value);
        var response = await ExchangeAsync(request, 8, cancellationToken);
        if (!response.AsSpan(0, 6).SequenceEqual(request.AsSpan(0, 6)))
            throw new InvalidDataException("气密仪未确认启动/停止指令。");
    }

    public async Task<ushort> ReadUInt16Async(ushort address, CancellationToken cancellationToken = default)
    {
        var response = await ExchangeAsync(BuildRead(address, 1), 7, cancellationToken);
        EnsureReadResponse(response, 2);
        return (ushort)((response[3] << 8) | response[4]);
    }

    private async Task<bool[]> ReadInputBitsAsync(ushort address, ushort count, CancellationToken cancellationToken)
    {
        var request = new List<byte> { _configuration.StationAddress, 0x02, (byte)(address >> 8), (byte)address, (byte)(count >> 8), (byte)count };
        AddCrc(request);
        var response = await ExchangeAsync(request.ToArray(), 6, cancellationToken);
        if (response[1] != 0x02 || response[2] != 1) throw new InvalidDataException("气密仪阶段状态返回格式无效。");
        return Enumerable.Range(0, count).Select(bit => (response[3] & (1 << bit)) != 0).ToArray();
    }

    public async Task WriteFloatAsync(ushort address, float value, CancellationToken cancellationToken = default)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes); // ABCD: Modbus network byte order
        // BYZ examples explicitly require swapping the two 16-bit words: CDAB on the wire.
        var payload = new[] { bytes[2], bytes[3], bytes[0], bytes[1] };
        await WriteMultipleAsync(address, payload, cancellationToken);
    }

    public async Task<float> ReadFloatAsync(ushort address, CancellationToken cancellationToken = default)
    {
        var response = await ExchangeAsync(BuildRead(address, 2), 9, cancellationToken);
        EnsureReadResponse(response, 4);
        var bytes = new[] { response[5], response[6], response[3], response[4] }; // CDAB -> ABCD
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToSingle(bytes, 0);
    }

    private async Task WriteMultipleAsync(ushort address, byte[] payload, CancellationToken cancellationToken)
    {
        var request = new List<byte> { _configuration.StationAddress, 0x10, (byte)(address >> 8), (byte)address, 0, 2, 4 };
        request.AddRange(payload);
        AddCrc(request);
        var response = await ExchangeAsync(request.ToArray(), 8, cancellationToken);
        if (response[0] != _configuration.StationAddress || response[1] != 0x10 || response[2] != (byte)(address >> 8) || response[3] != (byte)address)
            throw new InvalidDataException("气密仪未确认浮点参数写入。");
    }

    private async Task<byte[]> ExchangeAsync(byte[] request, int responseLength, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var port = _port;
            if (port is null || !port.IsOpen) throw new InvalidOperationException("气密仪未连接。");
            port.DiscardInBuffer();
            await port.BaseStream.WriteAsync(request, cancellationToken);
            await port.BaseStream.FlushAsync(cancellationToken);
            var response = new byte[responseLength];
            var received = 0;
            while (received < response.Length)
            {
                var readTask = port.BaseStream.ReadAsync(response.AsMemory(received), CancellationToken.None).AsTask();
                var completed = await Task.WhenAny(readTask, Task.Delay(_configuration.TimeoutMilliseconds, cancellationToken));
                if (completed != readTask)
                {
                    // SerialPort 的异步 ReadAsync 在 Windows 上并不总是响应取消令牌；关闭端口才能可靠结束挂起读取。
                    AbortPort(port);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new TimeoutException($"等待气密仪响应超时（{_configuration.TimeoutMilliseconds} ms）。");
                }
                var count = await readTask;
                if (count == 0) throw new EndOfStreamException("气密仪返回数据不完整。");
                received += count;
            }
            ValidateCrc(response);
            if (response[0] != _configuration.StationAddress) throw new InvalidDataException("气密仪返回站号不匹配。");
            if ((response[1] & 0x80) != 0) throw new InvalidDataException($"气密仪返回 Modbus 异常码 0x{response[2]:X2}。");
            return response;
        }
        finally { _gate.Release(); }
    }

    private void AbortPort(SerialPort port)
    {
        if (ReferenceEquals(_port, port)) _port = null;
        try { if (port.IsOpen) port.Close(); }
        catch { /* stopping/timeout must not be blocked by serial driver cleanup */ }
        try { port.Dispose(); }
        catch { /* stopping/timeout must not be blocked by serial driver cleanup */ }
    }

    private byte[] BuildRead(ushort address, ushort count)
    {
        var bytes = new List<byte> { _configuration.StationAddress, 0x03, (byte)(address >> 8), (byte)address, (byte)(count >> 8), (byte)count };
        AddCrc(bytes); return bytes.ToArray();
    }

    private byte[] BuildWriteSingle(ushort address, ushort value)
    {
        var bytes = new List<byte> { _configuration.StationAddress, 0x06, (byte)(address >> 8), (byte)address, (byte)(value >> 8), (byte)value };
        AddCrc(bytes); return bytes.ToArray();
    }

    private byte[] BuildWriteCoil(ushort address, bool value)
    {
        var bytes = new List<byte> { _configuration.StationAddress, 0x05, (byte)(address >> 8), (byte)address, value ? (byte)0xFF : (byte)0x00, 0x00 };
        AddCrc(bytes); return bytes.ToArray();
    }

    private static void EnsureReadResponse(byte[] response, int bytes) { if (response[1] != 0x03 || response[2] != bytes) throw new InvalidDataException("气密仪读取返回格式无效。"); }
    // BYD 时间寄存器单位为 0.1 秒：上位机 3.0 秒写入 30，回读 30 显示为 3.0 秒。
    private const decimal TimeRegisterScale = 10m;
    private static ushort TimeToRegister(decimal seconds, string label)
    {
        var registerValue = seconds * TimeRegisterScale;
        if (seconds <= 0 || registerValue != decimal.Truncate(registerValue) || registerValue > ushort.MaxValue)
            throw new InvalidDataException($"{label}必须为 0.1–6553.5 秒，且最小步进为 0.1 秒，才能同步至仪器。");
        return (ushort)registerValue;
    }
    private static decimal TimeFromRegister(ushort registerValue) => registerValue / TimeRegisterScale;
    private static void VerifyTime(decimal expectedSeconds, ushort actualRegisterValue, string label)
    {
        var actualSeconds = TimeFromRegister(actualRegisterValue);
        if (expectedSeconds != actualSeconds)
            throw new InvalidDataException($"气密仪回读{label}为 {actualSeconds:0.0} 秒（寄存器值 {actualRegisterValue}），与保存值 {expectedSeconds:0.0} 秒不一致。");
    }
    private static void Verify(ushort expected, ushort actual, string label) { if (expected != actual) throw new InvalidDataException($"气密仪回读{label}为 {actual}，与保存值 {expected} 不一致。"); }
    private static void Verify(float expected, float actual, string label) { if (Math.Abs(expected - actual) > 0.001f) throw new InvalidDataException($"气密仪回读{label}为 {actual:0.###}，与保存值 {expected:0.###} 不一致。"); }
    private static void AddCrc(List<byte> bytes) { var crc = Crc16(bytes.ToArray()); bytes.Add((byte)crc); bytes.Add((byte)(crc >> 8)); }
    private static void ValidateCrc(byte[] bytes) { var actual = (ushort)(bytes[^2] | bytes[^1] << 8); var expected = Crc16(bytes.AsSpan(0, bytes.Length - 2)); if (actual != expected) throw new InvalidDataException("气密仪返回 CRC 校验失败。"); }
    private static ushort Crc16(ReadOnlySpan<byte> bytes) { ushort crc = 0xFFFF; foreach (var value in bytes) { crc ^= value; for (var i = 0; i < 8; i++) crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1); } return crc; }
    public async ValueTask DisposeAsync() { await DisconnectAsync(); _gate.Dispose(); }
}

public sealed record ByzChannelMeasurement(int Channel, float PressureKpa, float LeakPa, ushort ResultCode);

public sealed record ByzChannelStageStatus(int Channel, bool Fixture, bool Filling, bool Balancing, bool Testing, bool Exhausting)
{
    public bool IsRunning => Fixture || Filling || Balancing || Testing || Exhausting;
    public string Description => string.Join("、", new[]
    {
        Fixture ? "夹具" : null,
        Filling ? "充气" : null,
        Balancing ? "平衡" : null,
        Testing ? "检测" : null,
        Exhausting ? "排气" : null
    }.Where(value => value is not null)) is { Length: > 0 } text ? text : "空闲";
}
