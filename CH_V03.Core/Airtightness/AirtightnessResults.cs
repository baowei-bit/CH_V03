using System.Text.Json;
using CH_V03.Core.Configuration;

namespace CH_V03.Core.Airtightness;

// Instrument adapters must provide a stable cycle identity and a terminal channel
// result. A polling sample or a timeout is not a completed product result.
public sealed record AirtightnessChannelResult(int Channel, decimal PressureKpa, decimal LeakPa, bool InstrumentPass);

public sealed class AirtightnessCycle
{
    public Guid Id { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string Operator { get; set; } = "";
    public AirtightnessConfiguration Recipe { get; set; } = new();
    public List<AirtightnessChannelResult> Channels { get; set; } = new();

    public bool IsPass(AirtightnessChannelResult result) => result.InstrumentPass
        && result.PressureKpa >= Recipe.MinimumPressureKpa && result.PressureKpa <= Recipe.MaximumPressureKpa
        && result.LeakPa <= Recipe.MaximumLeakPa;
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
