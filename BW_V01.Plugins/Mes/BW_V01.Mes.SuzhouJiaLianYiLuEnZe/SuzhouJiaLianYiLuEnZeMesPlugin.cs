using System.Net.Http.Json;
using System.Text.Json;
using BW_V01.Core.Mes;

namespace BW_V01.Mes.SuzhouJiaLianYiLuEnZe;

public sealed class SuzhouJiaLianYiLuEnZeMesPlugin : IMesPlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id => "SuzhouJiaLianYi.LuEnZe.Http";

    public string Name => "苏州嘉联益（卢恩泽）MES";

    public MesPluginCapabilities Capabilities { get; } = new()
    {
        SupportsConnectionTest = true,
        SupportsPreCheck = true,
        SupportsResultUpload = true,
        SupportsYieldControl = false,
        SupportsOfflineCache = true
    };

    public IReadOnlyList<MesPluginConfigField> GetConfigFields()
    {
        return new[]
        {
            Field("BaseUrl", "MES 根地址", "http://ot-mfcsmtetwebapi.mflex.com.cn/", true),
            Field("PreCheckEndpoint", "卡关接口", "api/ettestrecords/prevalidation", true),
            Field("UploadEndpoint", "结果上传接口", "api/ettestrecords", true),
            Field("ConnectionEndpoint", "连线测试接口", "api/ettestrecords/prevalidation"),
            Field("TimeoutSeconds", "超时秒数", "5", true),
            Field("TestType", "测试类型", "ICT"),
            Field("PrevTestType", "前站测试类型", "NULL"),
            Field("ProductName", "产品名称"),
            Field("SlotName", "槽位", "1"),
            Field("SubSlotName", "子槽位", "A"),
            Field("WorkArea", "区域"),
            Field("Site", "站点"),
            Field("Program", "程序名"),
            Field("Lot", "批次", "NULL"),
            Field("IpAddress", "设备 IP")
        };
    }

    public async Task<MesResult> TestConnectionAsync(MesPluginOptions options, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options);

        try
        {
            var endpoint = options.Get("ConnectionEndpoint", options.Get("PreCheckEndpoint", string.Empty));
            var uri = string.IsNullOrWhiteSpace(endpoint)
                ? GetBaseUrl(options)
                : BuildUri(options, endpoint, BuildPreCheckQuery(options, new MesCheckRequest
                {
                    Barcode = "TEST",
                    UserNo = options.Get("ModifyUser"),
                    StationNo = options.Get("Station")
                }));

            using var response = await client.GetAsync(uri, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            return response.IsSuccessStatusCode
                ? MesResult.Success($"MES 服务可访问：{uri}", rawResponse: raw)
                : MesResult.Fail($"MES 服务返回 HTTP {(int)response.StatusCode}", ((int)response.StatusCode).ToString(), raw);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"MES 连接失败：{ex.Message}");
        }
    }

    public Task<MesResult> LoginAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(MesResult.Success("苏州嘉联益（卢恩泽）MES 不需要单独登录"));
    }

    public async Task<MesResult> CheckBeforeTestAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options);

        try
        {
            var uri = BuildUri(options, options.Get("PreCheckEndpoint", "api/ettestrecords/prevalidation"), BuildPreCheckQuery(options, request));
            using var response = await client.GetAsync(uri, cancellationToken);
            return await ToMesResultAsync(response, "MES 卡关通过", "MES 卡关失败", cancellationToken);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"MES 卡关异常：{ex.Message}");
        }
    }

    public async Task<MesResult> UploadResultAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options);

        try
        {
            var uri = BuildUri(options, options.Get("UploadEndpoint", "api/ettestrecords"));
            var payload = BuildUploadPayload(options, request);
            using var response = await client.PostAsJsonAsync(uri, payload, JsonOptions, cancellationToken);
            return await ToMesResultAsync(response, "MES 上传成功", "MES 上传失败", cancellationToken);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"MES 上传异常：{ex.Message}");
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

    private static Dictionary<string, string> BuildPreCheckQuery(MesPluginOptions options, MesCheckRequest request)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["barcode"] = request.Barcode,
            ["operator"] = Value(options, "ModifyUser", request.UserNo),
            ["toolNumber"] = Value(options, "Tooling", options.Get("PackNo")),
            ["TestType"] = Value(options, "TestType", "ICT"),
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
        return new
        {
            barcode = request.Barcode,
            @operator = Value(options, "ModifyUser", request.UserNo),
            toolNumber = Value(options, "Tooling", request.PackNo),
            TestType = Value(options, "TestType", "ICT"),
            PrevTestType = Value(options, "PrevTestType", "NULL"),
            ProductName = Value(options, "ProductName"),
            SlotName = Value(options, "SlotName", "1"),
            subSlotName = Value(options, "SubSlotName", "A"),
            workArea = Value(options, "WorkArea"),
            site = Value(options, "Site", Value(options, "WorkArea")),
            program = Value(options, "Program", Value(options, "ProductName")),
            lot = Value(options, "Lot", "NULL"),
            ipAddress = Value(options, "IpAddress"),
            stationTime = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            startTime = request.StartTime.ToString("yyyy-MM-dd HH:mm:ss"),
            endTime = request.EndTime.ToString("yyyy-MM-dd HH:mm:ss"),
            result = IsPass(request.Result) ? "PASS" : "FAIL",
            errCode = FirstFail(request.TestItems, item => item.Name),
            errValue = FirstFail(request.TestItems, item => item.Value),
            remark = request.Remark,
            testItems = request.TestItems.Select((item, index) => new
            {
                stepNum = (index + 1).ToString(),
                itemName = item.Name,
                value = item.Value,
                unit = item.Unit,
                upperLimit = item.UpperLimit,
                lowerLimit = item.LowerLimit,
                result = IsPass(item.Result) ? "PASS" : "FAIL"
            }).ToList()
        };
    }

    private static async Task<MesResult> ToMesResultAsync(HttpResponseMessage response, string successMessage, string failMessage, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return MesResult.Fail($"{failMessage}：HTTP {(int)response.StatusCode} {response.ReasonPhrase}", ((int)response.StatusCode).ToString(), raw);
        }

        var business = ReadBusinessResult(raw);
        if (business.HasValue)
        {
            return business.Value
                ? MesResult.Success($"{successMessage}：{ReadMessage(raw)}", rawResponse: raw)
                : MesResult.Fail($"{failMessage}：{ReadMessage(raw)}", rawResponse: raw);
        }

        return MesResult.Success($"{successMessage}：HTTP {(int)response.StatusCode}", rawResponse: raw);
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
                if (root.TryGetProperty(name, out var value))
                {
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

            foreach (var name in new[] { "msgId", "code", "Code" })
            {
                if (root.TryGetProperty(name, out var value))
                {
                    return value.ToString() == "0";
                }
            }
        }
        catch
        {
            return raw.Contains("\"isSuccess\":true", StringComparison.OrdinalIgnoreCase)
                || raw.Contains("\"success\":true", StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    private static string ReadMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "MES 无返回内容";
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            foreach (var name in new[] { "message", "msgStr", "Message", "errorMessage" })
            {
                if (root.TryGetProperty(name, out var value) && !string.IsNullOrWhiteSpace(value.ToString()))
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

    private static Uri BuildUri(MesPluginOptions options, string relativeOrAbsolute, IReadOnlyDictionary<string, string>? query = null)
    {
        var uri = Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute)
            ? absolute
            : new Uri(GetBaseUrl(options), relativeOrAbsolute.TrimStart('/'));

        if (query is null || query.Count == 0)
        {
            return uri;
        }

        var builder = new UriBuilder(uri);
        var queryText = string.Join("&", query
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
        builder.Query = string.IsNullOrWhiteSpace(builder.Query)
            ? queryText
            : builder.Query.TrimStart('?') + "&" + queryText;
        return builder.Uri;
    }

    private static Uri GetBaseUrl(MesPluginOptions options)
    {
        var value = options.Get("BaseUrl", "http://ot-mfcsmtetwebapi.mflex.com.cn/");
        if (!value.EndsWith("/", StringComparison.Ordinal))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }

    private static HttpClient CreateClient(MesPluginOptions options)
    {
        var timeoutSeconds = int.TryParse(options.Get("TimeoutSeconds", "5"), out var seconds) ? seconds : 5;
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

    private static string FirstFail(IEnumerable<MesTestItem> items, Func<MesTestItem, string> selector)
    {
        return selector(items.FirstOrDefault(item => !IsPass(item.Result)) ?? new MesTestItem());
    }
}
