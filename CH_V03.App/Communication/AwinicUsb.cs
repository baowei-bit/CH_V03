using System.Buffers.Binary;
using System.IO;
using Windows.Security.Cryptography;
using Windows.Foundation;
using Windows.Devices.Enumeration;
using Windows.Devices.HumanInterfaceDevice;
using Windows.Storage;

namespace CH_V03.App.Communication;

internal sealed class AwinicUsb : IDisposable
{
    private HidDevice? _device;
    private int _generation;
    public bool Connected => _device != null;
    public void Dispose()
    {
        _generation++;
        var device = _device;
        _device = null;
        device?.Dispose();
    }

    internal async Task<string> ConnectAsync()
    {
        if (Connected) return "陪测板已连接";
        int generation = ++_generation;
        const string hidInterfaces = "System.Devices.InterfaceClassGuid:=\"{4D1E55B2-F16F-11CF-88CB-001111000030}\" AND System.Devices.InterfaceEnabled:=System.StructuredQueryType.Boolean#True";
        var devices = await DeviceInformation.FindAllAsync(hidInterfaces);
        var matches = devices.Where(device => device.Id.Contains("vid_344f&pid_1001", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length == 0) return "未发现 Awinic HID 接口（344F:1001）";
        if (matches.Length > 1) return "检测到多个匹配接口，请仅连接本工站的陪测板";
        foreach (var match in matches)
        {
            var device = await HidDevice.FromIdAsync(match.Id, FileAccessMode.ReadWrite);
            if (generation != _generation) { device?.Dispose(); return "陪测板连接已取消"; }
            _device = device;
            if (device != null) return $"已通过 USB 打开 {match.Name}，VID {device.VendorId:X4} / PID {device.ProductId:X4}。采样尚未启动";
        }
        return "已识别 Awinic，无法打开 HID 接口。请检查设备占用或访问权限";
    }

    private readonly SemaphoreSlim _wire = new(1);
    internal Task<uint> ReadAsync(ushort address, CancellationToken cancellationToken = default) => ExchangeAsync(address, null, cancellationToken);
    internal Task WriteAsync(ushort address, uint value, CancellationToken cancellationToken = default) => ExchangeAsync(address, value, cancellationToken);

    private async Task<uint> ExchangeAsync(ushort address, uint? value, CancellationToken cancellationToken)
    {
        var generation = _generation;
        await _wire.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _generation) throw new IOException("陪测板连接已改变，旧通讯已取消");
            var device = _device ?? throw new IOException("陪测板未连接");
            var reply = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            TypedEventHandler<HidDevice, HidInputReportReceivedEventArgs> receive = (_, args) =>
            {
                CryptographicBuffer.CopyToByteArray(args.Report.Data, out var data);
                reply.TrySetResult(data);
            };
            device.InputReportReceived += receive;
            try
            {
                var report = device.CreateOutputReport();
                var bytes = new byte[report.Data.Length];
                if (bytes.Length != 65) throw new IOException($"HID 输出报告长度异常：{bytes.Length}");
                var length = value.HasValue ? 20 : 16;
                bytes[1] = bytes[2] = 0x5a; bytes[3] = 4; bytes[4] = 1;
                bytes[5] = (byte)(length + 10); bytes[13] = 0x10;
                bytes[14] = value.HasValue ? (byte)0x23 : (byte)0x22; bytes[15] = (byte)length;
                bytes[17] = 1; bytes[18] = 2; bytes[19] = 4; bytes[21] = 0x12;
                bytes[23] = 1; bytes[26] = value.HasValue ? (byte)0 : (byte)1; bytes[27] = 1;
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(29), address);
                if (value.HasValue) BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(33), value.Value);
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(11), Crc(bytes, 13, length+4));
                report.Data = CryptographicBuffer.CreateFromByteArray(bytes);
                await device.SendOutputReportAsync(report).AsTask(cancellationToken).WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                var answer = await reply.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
                if (generation != _generation) throw new IOException("陪测板连接已改变，旧回包已丢弃");
                int off = answer.Length > 2 && answer[0] == 0 ? 1 : 0;
                if (answer.Length < off + 16 || answer[off] != 0x5a || answer[off+1] != 0x5a)
                    throw new IOException("陪测板回包帧头错误");
                int n = BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(off+14));
                if (off+16+n > answer.Length || BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(off+4)) != n+10 ||
                    Crc(answer,off+12,n+4) != BinaryPrimitives.ReadUInt16LittleEndian(answer.AsSpan(off+10)))
                    throw new IOException("陪测板回包长度或 CRC 错误");
                if (answer[off+13] != (value.HasValue ? 0xa3 : 0xa2))
                    throw new IOException($"陪测板应答 0x{answer[off+13]:X2}");
                if (value.HasValue) return 0;
                if (n < 20) throw new IOException("寄存器回包缺少数据");
                return BinaryPrimitives.ReadUInt32BigEndian(answer.AsSpan(off+32));
            }
            finally
            {
                // 拔线或主动断开后句柄可能已销毁，解绑不能覆盖原始异常。
                try { device.InputReportReceived -= receive; }
                catch (Exception) when (generation != _generation) { }
            }
        }
        catch
        {
            if (generation == _generation) Dispose();
            throw;
        }
        finally { _wire.Release(); }
    }
    private static ushort Crc(byte[] bytes, int start, int count)
    {
        ushort crc=0;
        for (int i=start;i<start+count;i++)
        {
            crc ^= bytes[i];
            for(int bit=0;bit<8;bit++) crc=(ushort)((crc>>1)^((crc&1)!=0?0xa001:0));
        }
        return crc;
    }
}
