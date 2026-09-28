using System.Net.Http.Json;
using System.Text.Json;
using CH_V03.Core.Mes;

namespace CH_V03.Mes.Bomin;

public sealed class BominMesPlugin : IMesPlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id => "Bomin.Http";

    public string Name => "Bomin HTTP MES";

    public MesPluginCapabilities Capabilities { get; } = new()
    {
        SupportsConnectionTest = true,
        SupportsLogin = true,
        SupportsPreCheck = true,
        SupportsResultUpload = true,
        SupportsOfflineCache = true
    };

    public IReadOnlyList<MesPluginConfigField> GetConfigFields()
    {
        return new[]
        {
            Field("LoginUrl", "Login URL", "http://ot-mfcsmtetwebapi.mflex.com.cn/api/etresources/etauthentication"),
            Field("PreCheckUrl", "Pre-check URL", "http://ot-mfcsmtetwebapi.mflex.com.cn/api/ettestrecords/prevalidation", true),
            Field("UploadUrl", "Upload URL", "http://ot-mfcsmtetwebapi.mflex.com.cn/api/ettestrecords", true),
            Field("TimeoutSeconds", "Timeout seconds", "3", true),
            Field("Role", "Login role", "0"),
            Field("TestType", "Test type", "FCT"),
            Field("PrevTestType", "Previous test type", "NULL"),
            Field("ProductName", "Product name"),
            Field("WorkArea", "Work area"),
            Field("Site", "Site"),
            Field("Program", "Program"),
            Field("Lot", "Lot", "NULL"),
            Field("IpAddress", "Machine IP", "10.1.226.219"),
            Field("ToolNumber", "Fixture number", "FS00035535-0001"),
            Field("SlotName", "Slot", "1"),
            Field("SubSlotName", "Sub slot", "A"),
            Field("ResourceName", "Resource name"),
            Field("MaterialLotNames", "Material lots, comma separated")
        };
    }

    public async Task<MesResult> TestConnectionAsync(MesPluginOptions options, CancellationToken cancellationToken = default)
    {
        return await CheckBeforeTestAsync(options, new MesCheckRequest
        {
            Barcode = "TEST",
            UserNo = options.Get("ModifyUser"),
            StationNo = options.Get("Station")
        }, cancellationToken);
    }

    public async Task<MesResult> LoginAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        var loginUrl = options.Get("LoginUrl");
        if (string.IsNullOrWhiteSpace(loginUrl))
        {
            return MesResult.Success("Bomin MES login skipped because LoginUrl is empty.");
        }

        using var client = CreateClient(options);
        try
        {
            var payload = new
            {
                userName = Value(options, "ModifyUser", request.UserNo),
                password = Value(options, "Password", request.Password),
                role = int.TryParse(options.Get("Role", "0"), out var role) ? role : 0
            };

            using var response = await client.PostAsJsonAsync(loginUrl, payload, JsonOptions, cancellationToken);
            return await ToMesResultAsync(response, "MES login succeeded", "MES login failed", cancellationToken);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"MES login exception: {ex.Message}");
        }
    }

    public async Task<MesResult> CheckBeforeTestAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options);
        try
        {
            var uri = BuildUri(options.Get("PreCheckUrl"), BuildPreCheckQuery(options, request));
            using var response = await client.GetAsync(uri, cancellationToken);
            return await ToMesResultAsync(response, "MES pre-check succeeded", "MES pre-check failed", cancellationToken);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"MES pre-check exception: {ex.Message}");
        }
    }

    public async Task<MesResult> UploadResultAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options);
        try
        {
            var payload = BuildUploadPayload(options, request);
            using var response = await client.PostAsJsonAsync(options.Get("UploadUrl"), payload, JsonOptions, cancellationToken);
            return await ToMesResultAsync(response, "MES upload succeeded", "MES upload failed", cancellationToken);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"MES upload exception: {ex.Message}");
        }
    }

    private static MesPluginConfigField Field(string key, string displayName, string defaultValue = "", bool required = false)
    {
        return new MesPluginConfigField
        {
            Key = key,
            DisplayName = displayName,
            DefaultValue = defaultValue,
            IsRequired = required
        };
    }

    private static IReadOnlyDictionary<string, string> BuildPreCheckQuery(MesPluginOptions options, MesCheckRequest request)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["barcode"] = request.Barcode,
            ["operator"] = Value(options, "ModifyUser", request.UserNo),
            ["toolNumber"] = Value(options, "ToolNumber", Value(options, "Tooling", options.Get("PackNo"))),
            ["TestType"] = Value(options, "TestType", "FCT"),
            ["PrevTestType"] = Value(options, "PrevTestType", "NULL"),
            ["ProductName"] = Value(options, "ProductName"),
            ["SlotName"] = Value(options, "SlotName", "1"),
            ["subSlotName"] = Value(options, "SubSlotName", "A"),
            ["workArea"] = Value(options, "WorkArea"),
            ["site"] = Value(options, "Site", Value(options, "WorkArea")),
            ["program"] = Value(options, "Program", Value(options, "ProductName")),
            ["lot"] = Value(options, "Lot", "NULL"),
            ["ipAddress"] = Value(options, "IpAddress"),
            ["stationTime"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
        };
    }

    private static object BuildUploadPayload(MesPluginOptions options, MesUploadRequest request)
    {
        var failedItem = request.TestItems.FirstOrDefault(item => !IsPass(item.Result));
        return new
        {
            barcode = request.Barcode,
            testType = Value(options, "TestType", "FCT"),
            testTime = request.StartTime.ToString("yyyy-MM-ddTHH:mm:ss"),
            testResult = IsPass(request.Result) ? "PASS" : "FAIL",
            @operator = Value(options, "ModifyUser", request.UserNo),
            workArea = Value(options, "WorkArea"),
            resourceName = Value(options, "ResourceName", Value(options, "ToolNumber")),
            ipAddress = Value(options, "IpAddress"),
            toolNumber = Value(options, "ToolNumber", Value(options, "Tooling", request.PackNo)),
            slotName = Value(options, "SlotName", "1"),
            subslotName = Value(options, "SubSlotName", "A"),
            errorCode = failedItem?.Name ?? string.Empty,
            errorValue = failedItem?.Value ?? string.Empty,
            productName = Value(options, "ProductName"),
            program = Value(options, "Program", Value(options, "ProductName")),
            socketInfo = (string?)null,
            rosalineInfo = (string?)null,
            testDetailId = Guid.NewGuid().ToString(),
            attribute1 = (string?)null,
            attribute2 = (string?)null,
            dartfishInfo = (string?)null,
            compressedTestDetails = (string?)null,
            extendInfo = (string?)null,
            testDetails = request.TestItems.Select(item => new
            {
                header = item.Name,
                upperLimit = item.UpperLimit,
                lowerLimit = item.LowerLimit,
                measureUnit = item.Unit,
                value = item.Value
            }).ToArray(),
            materialLotNames = SplitMaterialLots(Value(options, "MaterialLotNames"))
        };
    }

    private static string[]? SplitMaterialLots(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task<MesResult> ToMesResultAsync(HttpResponseMessage response, string successMessage, string failMessage, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return MesResult.Fail($"{failMessage}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}", ((int)response.StatusCode).ToString(), raw);
        }

        var success = ReadBusinessResult(raw);
        if (success.HasValue)
        {
            return success.Value
                ? MesResult.Success($"{successMessage}: {ReadMessage(raw)}", rawResponse: raw)
                : MesResult.Fail($"{failMessage}: {ReadMessage(raw)}", rawResponse: raw);
        }

        return MesResult.Success($"{successMessage}: HTTP {(int)response.StatusCode}", rawResponse: raw);
    }

    private static bool? ReadBusinessResult(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            foreach (var name in new[] { "isSuccess", "success", "result", "Result" })
            {
                if (!root.TryGetProperty(name, out var value))
                {
                    continue;
                }

                if (value.ValueKind == JsonValueKind.True)
                {
                    return true;
                }

                if (value.ValueKind == JsonValueKind.False)
                {
                    return false;
                }

                var text = value.ToString();
                if (IsPass(text) || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "NG", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }
        catch
        {
            return raw.Contains("\"isSuccess\":true", StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    private static string ReadMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "MES returned empty content.";
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            foreach (var name in new[] { "message", "msgStr", "Message", "errorMessage" })
            {
                if (document.RootElement.TryGetProperty(name, out var value) && !string.IsNullOrWhiteSpace(value.ToString()))
                {
                    return value.ToString();
                }
            }
        }
        catch
        {
            return raw;
        }

        return raw;
    }

    private static Uri BuildUri(string absoluteUrl, IReadOnlyDictionary<string, string> query)
    {
        var builder = new UriBuilder(absoluteUrl);
        builder.Query = string.Join("&", query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
        return builder.Uri;
    }

    private static HttpClient CreateClient(MesPluginOptions options)
    {
        var timeoutSeconds = int.TryParse(options.Get("TimeoutSeconds", "3"), out var seconds) ? seconds : 3;
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))
        };
    }

    private static string Value(MesPluginOptions options, string key, string defaultValue = "")
    {
        return options.Get(key, defaultValue);
    }

    private static bool IsPass(string value)
    {
        return string.Equals(value, "OK", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "PASS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase);
    }
}
