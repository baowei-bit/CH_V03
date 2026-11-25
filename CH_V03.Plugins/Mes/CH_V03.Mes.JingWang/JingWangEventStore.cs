using System.Text.Json;
using CH_V03.Core.Mes;

namespace CH_V03.Mes.JingWang;

internal sealed class JingWangEventStore
{
    private readonly string _root;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JingWangEventStore(MesPluginOptions options) =>
        _root = options.Get("JournalDirectory", Path.Combine(AppContext.BaseDirectory, "data", "jingwang"));

    public bool IsCompleted(string id) => File.Exists(PathFor("events-completed", id));

    public string Save(string kind, string label, IReadOnlyList<Dictionary<string, string>> rows, string? id = null)
    {
        id ??= Guid.NewGuid().ToString("N");
        Write("events-pending", new EventRecord(id, kind, label, DateTime.Now, rows));
        return id;
    }

    public EventRecord? Load(string id)
    {
        if (id.Length != 32 || !id.All(Uri.IsHexDigit)) return null;
        var file = PathFor("events-pending", id);
        if (!File.Exists(file)) return null;
        try { return JsonSerializer.Deserialize<EventRecord>(File.ReadAllText(file), JsonOptions); }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    public void Complete(EventRecord record)
    {
        Write("events-completed", record);
        var pending = PathFor("events-pending", record.Id);
        if (File.Exists(pending)) File.Delete(pending);
    }

    public bool Archive(string id)
    {
        if (id.Length != 32 || !id.All(Uri.IsHexDigit)) return false;
        var source = PathFor("events-pending", id);
        if (!File.Exists(source)) return false;
        var destination = PathFor("events-archived", id);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination, overwrite: true);
        return true;
    }

    public IReadOnlyList<JingWangPendingRecord> GetPending()
    {
        var directory = Path.Combine(_root, "events-pending");
        if (!Directory.Exists(directory)) return [];
        var records = new List<JingWangPendingRecord>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var item = JsonSerializer.Deserialize<EventRecord>(File.ReadAllText(file), JsonOptions);
                if (item is not null) records.Add(new(item.Id, item.Label, "待写入" + item.Kind, item.CreatedAt, "请核对后补写数据库"));
            }
            catch (Exception ex) when (ex is IOException or JsonException) { }
        }
        return records;
    }

    private void Write(string category, EventRecord record)
    {
        var path = PathFor(category, record.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, record, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string PathFor(string category, string id) => Path.Combine(_root, category, id + ".json");
}

internal sealed record EventRecord(string Id, string Kind, string Label, DateTime CreatedAt,
    IReadOnlyList<Dictionary<string, string>> Rows);
