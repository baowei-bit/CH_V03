using System.Text.Json;
using CH_V03.Core.Mes;

namespace CH_V03.Mes.JingWang;

internal sealed class JingWangRecordStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root;
    private readonly Dictionary<string, string> _productionFields;

    public JingWangRecordStore(MesPluginOptions options)
    {
        _root = options.Get("JournalDirectory", Path.Combine(AppContext.BaseDirectory, "data", "jingwang"));
        _productionFields = new[] { "WorkOrder", "SpecCode", "Product", "Container", "SetId", "PanelSequence", "EquipmentCode" }
            .Where(key => !string.IsNullOrWhiteSpace(options.Get(key)))
            .ToDictionary(key => key, key => options.Get(key), StringComparer.OrdinalIgnoreCase);
    }

    public bool IsCompleted(string id) => File.Exists(PathFor("completed", id));

    public MesUploadRequest? LoadRequest(string category, string id)
    {
        if (id.Length != 32 || !id.All(Uri.IsHexDigit)) return null;
        var path = PathFor(category, id);
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<Record>(File.ReadAllText(path), JsonOptions)?.Request; }
        catch (Exception ex) when (ex is IOException or JsonException) { return null; }
    }

    public void SavePending(string id, MesUploadRequest request, string status, string message) =>
        Save("pending", new Record(id, request.Barcode, status, DateTime.Now, message, request, ProductionFields: _productionFields));

    public void MarkCompleted(string id, MesUploadRequest request, string message)
    {
        Save("completed", new Record(id, request.Barcode, "已写入", DateTime.Now, message, request, ProductionFields: _productionFields));
        var pending = PathFor("pending", id);
        if (File.Exists(pending)) File.Delete(pending);
    }

    public void SaveReview(string id, MesUploadRequest request, string status, string message, string? precheckStatus = null) =>
        Save("review", new Record(id, request.Barcode, status, DateTime.Now, message, request, precheckStatus ?? request.PrecheckStatus, _productionFields));

    public IReadOnlyDictionary<string, string> LoadProductionFields(string category, string id)
    {
        if (id.Length != 32 || !id.All(Uri.IsHexDigit)) return new Dictionary<string, string>();
        var path = PathFor(category, id);
        return File.Exists(path)
            ? JsonSerializer.Deserialize<Record>(File.ReadAllText(path), JsonOptions)?.ProductionFields ?? new Dictionary<string, string>()
            : new Dictionary<string, string>();
    }

    public string? LoadPrecheckStatus(string id)
    {
        if (id.Length != 32 || !id.All(Uri.IsHexDigit)) return null;
        var path = PathFor("review", id);
        return File.Exists(path) ? JsonSerializer.Deserialize<Record>(File.ReadAllText(path), JsonOptions)?.PrecheckStatus : null;
    }

    public void SaveApiOutcome(string id, MesUploadRequest request, string status, string message, string precheckStatus) =>
        Save(Path.Combine("api-history", id), new Record(Guid.NewGuid().ToString("N"), request.Barcode, status, DateTime.Now, message, request, precheckStatus, _productionFields));

    public void ClearReview(string id)
    {
        var path = PathFor("review", id);
        if (File.Exists(path)) File.Delete(path);
    }

    public bool Archive(string id)
    {
        if (id.Length != 32 || !id.All(Uri.IsHexDigit)) return false;
        foreach (var category in new[] { "pending", "review" })
        {
            var source = PathFor(category, id);
            if (!File.Exists(source)) continue;
            var destination = PathFor("archived", id + "-" + category);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(source, destination, overwrite: true);
            return true;
        }
        return false;
    }

    public IReadOnlyList<JingWangPendingRecord> GetPending()
    {
        var result = new List<JingWangPendingRecord>();
        foreach (var category in new[] { "pending", "review" })
        {
            var directory = Path.Combine(_root, category);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<Record>(File.ReadAllText(file), JsonOptions);
                    if (record is not null) result.Add(new(record.Id, record.Barcode, record.Status, record.CreatedAt, record.Message,
                        record.Request.Result, record.Request.Files.FirstOrDefault()?.FilePath ?? "",
                        record.ProductionFields?.GetValueOrDefault("WorkOrder", "") ?? "",
                        record.ProductionFields?.GetValueOrDefault("Container", "") ?? ""));
                }
                catch (IOException) { }
                catch (JsonException) { }
            }
        }
        return result.OrderByDescending(x => x.CreatedAt).ToList();
    }

    private void Save(string category, Record record)
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
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private string PathFor(string category, string id) => Path.Combine(_root, category, id + ".json");

    private sealed record Record(string Id, string Barcode, string Status, DateTime CreatedAt, string Message, MesUploadRequest Request,
        string? PrecheckStatus = null, Dictionary<string, string>? ProductionFields = null);
}
