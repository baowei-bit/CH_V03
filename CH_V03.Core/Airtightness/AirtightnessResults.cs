using System.Globalization;
using System.Text;
using System.Text.Json;
using CH_V03.Core.Configuration;

namespace CH_V03.Core.Airtightness;

// Instrument adapters must provide a stable cycle identity and a terminal channel
// result. A polling sample or a timeout is not a completed product result.
public sealed record AirtightnessChannelResult(int Channel, decimal PressureKpa, decimal LeakPa, bool InstrumentPass, ushort InstrumentResultCode = 0);

public sealed class AirtightnessCycle
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    /// <summary>双通道测试完成并生成正式 CSV 时的当前时间。</summary>
    public DateTimeOffset CompletedAt { get; set; }
    public string Operator { get; set; } = "";
    public string Channel1Barcode { get; set; } = "";
    public string Channel2Barcode { get; set; } = "";
    public AirtightnessConfiguration Recipe { get; set; } = new();
    public List<AirtightnessChannelResult> Channels { get; set; } = new();

    public bool IsPass(AirtightnessChannelResult result) => result.InstrumentPass;
}

public sealed record AirtightnessStatistics(int Pass, int Fail, int Channel1Pass, int Channel1Fail, int Channel2Pass, int Channel2Fail)
{
    public int Total => Pass + Fail;
    public double Yield => Total == 0 ? 0 : 100d * Pass / Total;

    public static AirtightnessStatistics Calculate(IEnumerable<AirtightnessCycle> records, AirtightnessCountingMode mode)
    {
        int pass = 0, fail = 0, c1p = 0, c1f = 0, c2p = 0, c2f = 0;
        foreach (var cycle in records.Where(r => r.Recipe.CountingMode == mode))
        {
            foreach (var result in cycle.Channels)
            {
                var ok = cycle.IsPass(result);
                if (result.Channel == 1) { if (ok) c1p++; else c1f++; }
                else { if (ok) c2p++; else c2f++; }
                if (mode == AirtightnessCountingMode.SeparateProducts) { if (ok) pass++; else fail++; }
            }
            if (mode == AirtightnessCountingMode.JointProduct && cycle.Channels.Count == 2)
            {
                if (cycle.Channels.All(cycle.IsPass)) pass++; else fail++;
            }
        }
        return new(pass, fail, c1p, c1f, c2p, c2f);
    }
}

public sealed class AirtightnessResultStore
{
    private readonly string _directory;
    private readonly object _gate = new();
    public AirtightnessResultStore(string directory) => _directory = directory;

    public IReadOnlyList<AirtightnessCycle> LoadAll()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_directory)) return Array.Empty<AirtightnessCycle>();
            return Directory.EnumerateFiles(_directory, "*.json").Select(Read).OrderByDescending(r => r.StartedAt).ToList();
        }
    }

    /// <summary>保存完整双通道正式记录，并在测试记录目录按日追加一行 CSV。</summary>
    public AirtightnessCycle RecordCompletedCycle(Guid cycleId, DateTimeOffset startedAt, string operatorName,
        AirtightnessConfiguration recipe, string? channel1Barcode, string? channel2Barcode,
        AirtightnessChannelResult channel1, AirtightnessChannelResult channel2)
    {
        if (cycleId == Guid.Empty) throw new ArgumentException("测试批次标识不能为空。");
        if (channel1.Channel != 1 || channel2.Channel != 2) throw new ArgumentException("完整记录必须包含 CH1、CH2 两个通道。");
        recipe.Validate();

        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var path = Path.Combine(_directory, cycleId.ToString("N") + ".json");
            var cycle = File.Exists(path) ? Read(path) : new AirtightnessCycle
            {
                Id = cycleId,
                StartedAt = startedAt,
                CompletedAt = DateTimeOffset.Now,
                Operator = operatorName,
                Channel1Barcode = (channel1Barcode ?? string.Empty).Trim(),
                Channel2Barcode = (channel2Barcode ?? string.Empty).Trim(),
                Recipe = ConfigurationStore.Clone(recipe)
            };

            if (cycle.StartedAt != startedAt || cycle.Operator != operatorName ||
                JsonSerializer.Serialize(cycle.Recipe) != JsonSerializer.Serialize(recipe))
                throw new InvalidOperationException("同一轮测试不能修改参数、统计模式或操作员。");
            if (cycle.Channels.Count > 0 && !cycle.Channels.OrderBy(x => x.Channel).SequenceEqual(new[] { channel1, channel2 }))
                throw new InvalidOperationException("已保存测试记录不能被覆盖。");

            cycle.Channels = new List<AirtightnessChannelResult> { channel1, channel2 };
            // 兼容升级前没有完成时间的 JSON 记录；重传同一正式结果时保留原时间。
            if (cycle.CompletedAt == default) cycle.CompletedAt = DateTimeOffset.Now;
            AirtightnessStore.WriteAtomic(path, cycle);
            AppendCsvIfNeeded(cycle);
            return cycle;
        }
    }

    private const string CsvHeader = "测试批次,测试当前时间,产品名称,统计模式,操作员,CH1条码,CH2条码,CH1压力(kPa),CH1泄漏(Pa),CH1仪器结果码,CH1结果,CH2压力(kPa),CH2泄漏(Pa),CH2仪器结果码,CH2结果,双通道结果";

    public string GetCsvFilePath(DateTimeOffset startedAt) => ResolveCsvFilePath(startedAt);

    private string ResolveCsvFilePath(DateTimeOffset startedAt)
    {
        var date = startedAt.LocalDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var standardPath = Path.Combine(_directory, $"气密测试记录_{date}.csv");
        if (!File.Exists(standardPath)) return standardPath;
        var existingHeader = File.ReadLines(standardPath).FirstOrDefault();
        return string.Equals(existingHeader, CsvHeader, StringComparison.Ordinal)
            ? standardPath
            : Path.Combine(_directory, $"气密测试记录_{date}_V2.csv");
    }

    private void AppendCsvIfNeeded(AirtightnessCycle cycle)
    {
        var path = GetCsvFilePath(cycle.StartedAt);
        var cycleId = cycle.Id.ToString("N");
        if (File.Exists(path) && File.ReadLines(path).Skip(1).Any(line => line.StartsWith(cycleId + ",", StringComparison.Ordinal))) return;

        var channel1 = cycle.Channels.Single(channel => channel.Channel == 1);
        var channel2 = cycle.Channels.Single(channel => channel.Channel == 2);
        var pass1 = cycle.IsPass(channel1);
        var pass2 = cycle.IsPass(channel2);
        var values = new[]
        {
            cycleId,
            cycle.CompletedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            cycle.Recipe.ProductName,
            cycle.Recipe.CountingMode == AirtightnessCountingMode.SeparateProducts ? "双产品" : "单产品",
            cycle.Operator,
            cycle.Channel1Barcode,
            cycle.Channel2Barcode,
            channel1.PressureKpa.ToString("0.000", CultureInfo.InvariantCulture),
            channel1.LeakPa.ToString("0.00", CultureInfo.InvariantCulture),
            channel1.InstrumentResultCode.ToString(CultureInfo.InvariantCulture),
            pass1 ? "PASS" : "FAIL",
            channel2.PressureKpa.ToString("0.000", CultureInfo.InvariantCulture),
            channel2.LeakPa.ToString("0.00", CultureInfo.InvariantCulture),
            channel2.InstrumentResultCode.ToString(CultureInfo.InvariantCulture),
            pass2 ? "PASS" : "FAIL",
            pass1 && pass2 ? "PASS" : "FAIL"
        };
        if (!File.Exists(path)) File.WriteAllText(path, CsvHeader + Environment.NewLine, new UTF8Encoding(true));
        File.AppendAllText(path, string.Join(",", values.Select(value => EscapeCsv(value))) + Environment.NewLine, new UTF8Encoding(false));
    }

    private static string EscapeCsv(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0
        ? '"' + value.Replace("\"", "\"\"") + '"'
        : value;
    public AirtightnessCycle Record(Guid cycleId, DateTimeOffset startedAt, string operatorName,
        AirtightnessConfiguration recipe, AirtightnessChannelResult result)
    {
        if (cycleId == Guid.Empty) throw new ArgumentException("测试批次标识不能为空。");
        if (result.Channel is not (1 or 2)) throw new ArgumentException("通道必须是 1 或 2。");
        recipe.Validate();
        lock (_gate)
        {
            var path = Path.Combine(_directory, cycleId.ToString("N") + ".json");
            var cycle = File.Exists(path) ? Read(path) : new AirtightnessCycle
            {
                Id = cycleId, StartedAt = startedAt, Operator = operatorName,
                Recipe = ConfigurationStore.Clone(recipe)
            };
            if (cycle.StartedAt != startedAt || cycle.Operator != operatorName ||
                JsonSerializer.Serialize(cycle.Recipe) != JsonSerializer.Serialize(recipe))
                throw new InvalidOperationException("同一轮测试不能修改参数、统计模式或操作员。");
            var existing = cycle.Channels.SingleOrDefault(c => c.Channel == result.Channel);
            if (existing is not null)
            {
                if (existing != result) throw new InvalidOperationException("已保存通道结果不能被覆盖。");
                return cycle;
            }
            cycle.Channels.Add(result);
            AirtightnessStore.WriteAtomic(path, cycle);
            return cycle;
        }
    }

    private static AirtightnessCycle Read(string path)
    {
        var cycle = JsonSerializer.Deserialize<AirtightnessCycle>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"气密记录为空：{path}");
        cycle.Recipe.Validate();
        if (cycle.Id == Guid.Empty || Path.GetFileNameWithoutExtension(path) != cycle.Id.ToString("N") ||
            cycle.Channels.Count is < 1 or > 2 || cycle.Channels.Any(c => c.Channel is not (1 or 2)) ||
            cycle.Channels.Select(c => c.Channel).Distinct().Count() != cycle.Channels.Count)
            throw new InvalidDataException($"气密记录格式错误：{path}");
        return cycle;
    }
}
