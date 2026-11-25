using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using CH_V03.Core.ResultExport;

namespace CH_V03.App.Communication;

internal static class AwinicMaintenance
{
    internal static List<(ushort Address, uint Value)> ReadRegisters()
    {
        bool version3 = Yidong2Exporter.UsesVersion3;
        var fileName = version3 ? "AW93305DNR_(AW93804QNR_DEBUG)_CFG_tws_20260130.txt" : "test.txt";
        var lines = File.ReadAllLines(Path.Combine(Yidong2Exporter.InstallationDirectory(version3), "data", "txt", fileName));
        var writes = new List<(ushort, uint)> { (0xfff4, 0x3c00d103) };
        foreach (var line in lines.Select(s => s.Trim()))
        {
            if (line is "" or "}" or "};" or "static const uint32_t aw93xxx_reg_default[] = {") continue;
            var pair = Regex.Match(line, @"^0[xX]([0-9A-Fa-f]{1,4})\s*,\s*0[xX]([0-9A-Fa-f]{1,8})\s*,?$");
            if (!pair.Success) throw new InvalidDataException("寄存器配置格式错误：" + line);
            writes.Add((ushort.Parse(pair.Groups[1].Value, NumberStyles.HexNumber),
                uint.Parse(pair.Groups[2].Value, NumberStyles.HexNumber)));
        }
        if (writes.Count == 1) throw new InvalidDataException("寄存器配置为空");
        return writes;
    }

    internal static List<(ushort Address, uint Value)> ReadFirmware()
    {
        var file = File.ReadAllBytes(Path.Combine(Yidong2Exporter.InstallationDirectory(Yidong2Exporter.UsesVersion3),
            "data", "hex", "AW9380X_SOC_FW_V4.1.3.5.hex"));
        // This profile uses the single contiguous boot RAM image supplied with GUI 1.0.0.1.
        if (Convert.ToHexString(SHA256.HashData(file)) != "26C866563234AD120909B98822E357BEDE7C0FD2949C2287AC59E421369BBD00")
            throw new InvalidDataException("固件与已适配的 V4.1.3.5 文件不一致");
        var image = new List<byte>();
        foreach (var line in System.Text.Encoding.ASCII.GetString(file).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var record = Convert.FromHexString(line.Substring(1));
            if (record[3] == 0) image.AddRange(record.AsSpan(4, record[0]).ToArray());
        }
        var data = image.ToArray();
        var writes = new List<(ushort, uint)> {
            (0xfff4, 0x3c00d11f), (0xc400, 0x21660000), (0xf800, 0x10100),
            (0xffe4, 0x3c000000), (0x4ff8, 0x21662166)
        };
        uint sum = 0;
        for (int offset = 0; offset < 0x2ff8; offset += 4)
        {
            uint word = offset < data.Length ? BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) : uint.MaxValue;
            sum = unchecked(sum + word);
            writes.Add(((ushort)(0x2000 + offset), word));
        }
        writes.Add((0x4ffc, unchecked(0u - sum)));
        writes.Add((0xf800, 0x100));
        writes.Add((0xff0c, 0));
        return writes;
    }
}
