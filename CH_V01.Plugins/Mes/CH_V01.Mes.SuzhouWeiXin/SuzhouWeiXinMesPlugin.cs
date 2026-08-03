using System.Net.Http.Json;
using System.Text.Json;
using CH_V01.Core.Mes;

namespace CH_V01.Mes.SuzhouWeiXin;

public sealed class SuzhouWeiXinMesPlugin : IMesPlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id => "SuzhouWeiXin.Http";

    public string Name => "苏州维信 HTTP MES";

    public MesPluginCapabilities Capabilities { get; } = new()
    {
        SupportsConnectionTest = true,
        SupportsPreCheck = true,
        SupportsResultUpload = true,
        SupportsOfflineCache = true
    };

    public IReadOnlyList<MesPluginConfigField> GetConfigFields()
    {
        return new[]
        {
            Field("PreCheckUrl", "卡关接口", "http://ot-mfcsmtetwebapi.mflex.com.cn/api/ettestrecords/prevalidation", true),
            Field("UploadUrl", "结果上传接口", "http://ot-mfcsmtetwebapi.mflex.com.cn/api/ettestrecords", true),
            Field("TimeoutSeconds", "超时秒数", "3", true),
            Field("ConnectionTestBarcode", "测试连接条码", "TEST"),
            Field("TestType", "测试类型", "ICT"),
            Field("PrevTestType", "前站测试类型", "NULL"),
            Field("ProductName", "产品名称", "18165"),
            Field("WorkArea", "车间"),
            Field("Site", "厂区"),
            Field("Program", "测试程序/产品名", "18165"),
            Field("Lot", "流程单", "NULL"),
            Field("IpAddress", "机台 IP"),
            Field("ToolNumber", "治具编号"),
            Field("SlotName", "穴位", "1"),
            Field("SubSlotName", "子穴位", "A"),
            Field("ResourceName", "资源/设备名"),
            Field("MaterialLotNames", "物料批次，逗号分隔")
        };
    }

    public Task<MesResult> TestConnectionAsync(MesPluginOptions options, CancellationToken cancellationToken = default)
    {
        var preCheckUrl = options.Get("PreCheckUrl");
        var uploadUrl = options.Get("UploadUrl");
        if (!Uri.TryCreate(preCheckUrl, UriKind.Absolute, out _))
        {
            return Task.FromResult(MesResult.Fail($"MES 卡关接口配置无效：{preCheckUrl}"));
        }

        if (!Uri.TryCreate(uploadUrl, UriKind.Absolute, out _))
        {
            return Task.FromResult(MesResult.Fail($"MES 上传接口配置无效：{uploadUrl}"));
        }

        return Task.FromResult(MesResult.Success("苏州维信 MES 已启用，扫码后使用真实条码执行卡关"));
    }

    public Task<MesResult> LoginAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(MesResult.Success("苏州维信 MES 不需要单独登录"));
    }

    public async Task<MesResult> CheckBeforeTestAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options);
        try
        {
            var uri = BuildUri(options.Get("PreCheckUrl"), BuildPreCheckQuery(options, request));
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
            var payload = BuildUploadPayload(options, request);
            using var response = await client.PostAsJsonAsync(options.Get("UploadUrl"), payload, JsonOptions, cancellationToken);
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

    private static IReadOnlyDictionary<string, string> BuildPreCheckQuery(MesPluginOptions options, MesCheckRequest request)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["barcode"] = request.Barcode,
            ["operator"] = Value(options, "ModifyUser", request.UserNo),
            ["toolNumber"] = Value(options, "ToolNumber", Value(options, "Tooling", options.Get("PackNo"))),
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
        var failedItem = request.TestItems.FirstOrDefault(item => !IsPass(item.Result));
        return new
        {
            barcode = request.Barcode,
            testType = Value(options, "TestType", "ICT"),
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
            return MesResult.Fail($"{failMessage}：HTTP {(int)response.StatusCode} {response.ReasonPhrase}", ((int)response.StatusCode).ToString(), raw);
        }

        var success = ReadBusinessResult(raw);
        if (success.HasValue)
        {
            return success.Value
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
            return "MES 无返回内容";
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
