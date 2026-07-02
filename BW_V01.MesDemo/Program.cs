using System.Collections.Concurrent;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.WriteIndented = true;
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});

var app = builder.Build();
var records = new ConcurrentQueue<MesRequestLog>();

app.MapGet("/", () => Results.Json(new
{
    service = "BW_V01 MES Demo",
    scenario = "SuzhouWeiXin.Http",
    preCheckUrl = "/api/ettestrecords/prevalidation",
    uploadUrl = "/api/ettestrecords",
    healthUrl = "/health",
    logsUrl = "/api/demo/logs"
}));

app.MapGet("/health", () => Results.Json(new
{
    isSuccess = true,
    message = "BW_V01 MES Demo is running",
    time = DateTime.Now
}));

app.MapGet("/api/ettestrecords/prevalidation", (HttpRequest request) =>
{
    var query = request.Query.ToDictionary(item => item.Key, item => item.Value.ToString(), StringComparer.OrdinalIgnoreCase);
    var barcode = Get(query, "barcode");
    var testType = Get(query, "TestType");
    var productName = Get(query, "ProductName");
    var toolNumber = Get(query, "toolNumber");

    var failureReason = ValidatePreCheck(barcode);
    var success = string.IsNullOrWhiteSpace(failureReason);
    var message = success
        ? $"Prevalidation OK. Barcode={barcode}, Product={productName}, TestType={testType}, Tool={toolNumber}"
        : failureReason;

    records.Enqueue(MesRequestLog.From("GET", request.Path, success, message, query));
    Trim(records);

    return Results.Json(new
    {
        isSuccess = success,
        success,
        result = success ? "PASS" : "NG",
        message,
        serverTime = DateTime.Now,
        data = new
        {
            barcode,
            productName,
            testType,
            toolNumber
        }
    });
});

app.MapPost("/api/ettestrecords", async (HttpRequest request) =>
{
    using var document = await JsonDocument.ParseAsync(request.Body);
    var root = document.RootElement.Clone();
    var barcode = ReadString(root, "barcode");
    var testResult = ReadString(root, "testResult");
    var testType = ReadString(root, "testType");
    var toolNumber = ReadString(root, "toolNumber");
    var errorCode = ReadString(root, "errorCode");
    var testDetailsCount = CountArray(root, "testDetails");

    var failureReason = ValidateUpload(barcode, testResult);
    var success = string.IsNullOrWhiteSpace(failureReason);
    var message = success
        ? $"Upload OK. Barcode={barcode}, Result={testResult}, Details={testDetailsCount}"
        : failureReason;

    records.Enqueue(MesRequestLog.From("POST", request.Path, success, message, root));
    Trim(records);

    return Results.Json(new
    {
        isSuccess = success,
        success,
        result = success ? "PASS" : "NG",
        message,
        msgStr = message,
        serverTime = DateTime.Now,
        data = new
        {
            barcode,
            testResult,
            testType,
            toolNumber,
            errorCode,
            testDetailsCount
        }
    });
});

app.MapGet("/api/demo/logs", () => Results.Json(records.Reverse().Take(50)));

app.MapDelete("/api/demo/logs", () =>
{
    while (records.TryDequeue(out _))
    {
    }

    return Results.Json(new
    {
        isSuccess = true,
        message = "Logs cleared"
    });
});

app.Run();

static string Get(IReadOnlyDictionary<string, string> values, string key, string defaultValue = "")
{
    return values.TryGetValue(key, out var value) ? value : defaultValue;
}

static string ValidatePreCheck(string barcode)
{
    if (string.IsNullOrWhiteSpace(barcode))
    {
        return "Barcode is required";
    }

    if (barcode.Contains("NG", StringComparison.OrdinalIgnoreCase)
        || barcode.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
    {
        return $"Barcode blocked by demo rule: {barcode}";
    }

    return string.Empty;
}

static string ValidateUpload(string barcode, string testResult)
{
    if (string.IsNullOrWhiteSpace(barcode))
    {
        return "Barcode is required";
    }

    if (string.Equals(testResult, "FAIL", StringComparison.OrdinalIgnoreCase)
        || string.Equals(testResult, "NG", StringComparison.OrdinalIgnoreCase))
    {
        return $"Upload received FAIL result. Barcode={barcode}";
    }

    return string.Empty;
}

static string ReadString(JsonElement root, string propertyName)
{
    return root.TryGetProperty(propertyName, out var value) ? value.ToString() : string.Empty;
}

static int CountArray(JsonElement root, string propertyName)
{
    return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.GetArrayLength()
        : 0;
}

static void Trim(ConcurrentQueue<MesRequestLog> records)
{
    while (records.Count > 100 && records.TryDequeue(out _))
    {
    }
}

internal sealed record MesRequestLog(
    DateTime Time,
    string Method,
    string Path,
    bool IsSuccess,
    string Message,
    object Payload)
{
    public static MesRequestLog From(string method, string path, bool isSuccess, string message, object payload)
    {
        return new MesRequestLog(DateTime.Now, method, path, isSuccess, message, payload);
    }
}
