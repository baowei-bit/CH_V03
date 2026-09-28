using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CH_V03.Core.Mes;

namespace CH_V03.Mes.YiDongEap;

/// <summary>
/// 江西奕东后段 TIMS 数据传输接口。
/// 该插件覆盖扫码前过站校验、测试后过站报工和可选的治具/模具计数上报。
/// </summary>
public sealed class YiDongEapMesPlugin : IMesPlugin
{
    private const string DataTransferPath = "/api/Ats/TimsDataTrans";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = false
    };

    public string Id => "YiDongEap.Tims";

    public string Name => "奕东 EAP（TIMS 后段接口）";

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
            Field("Endpoint", "EAP接口地址", "http://127.0.0.1:8001/api/Ats/TimsDataTrans", true),
            Field("TimeoutSeconds", "超时秒数", "5", true),
            Field("CustomerCode", "客户代码 sMesUser", "JXYD", true),
            Field("Language", "语言 sLanguage", "S", true),
            Field("FactoryNo", "工厂代码 sFactoryNo", "0", true),
            Field("ApiUser", "EAP用户 sUserNo", "sa", true),
            Field("ClientIp", "客户端IP sClientIp"),
            Field("LotCardNo", "LOT卡号", "", true),
            Field("TerminalNo", "工位代码 TERMINAL_NO", "", true),
            Field("EquipmentCode", "设备编码 EQUIPCODE"),
            Field("FixtureNo", "治具/模具代码 EQUIPMENT_NO"),
            Field("BarcodeType", "条码类型 TYPE", "SN", true),
            Field("CarrierNo", "载具条码 CARRIER_NO"),
            Field("ProgramName", "设备程序名 PRO_NAME"),
            Field("ProgramUpdateDate", "程序更新时间 PRO_UPDATE_DATE"),
            Field("PressureValue", "压力值 PRESSURE_VALUE"),
            Field("PressingDate", "压合时间 PRESSING_DATE"),
            Field("Temperature", "温度 TEMPERATURE"),
            Field("CheckFeedMaterial", "校验欠料 IS_CHECK_FEED_MATERIAL", "N", true),
            Field("CheckStation", "校验工艺路线 IS_CHECK_STATION", "Y", true),
            Field("DeductMaterial", "过站扣料 IS_DEDUCT", "N", true),
            Field("MaterialDetails", "扣料明细 FID:QTY，逗号分隔"),
            Field("EnableFixtureCount", "启用治具累计次数上报 Y/N", "N", true),
            Field("FixtureCount", "本次增加使用次数", "1", true)
        };
    }

    public Task<MesResult> TestConnectionAsync(MesPluginOptions options, CancellationToken cancellationToken = default)
    {
        var validation = ValidateBaseOptions(options);
        if (validation is not null)
        {
            return Task.FromResult(validation);
        }

        return Task.FromResult(MesResult.Success("奕东 EAP 配置有效；实际连通性将在扫码校验时验证"));
    }

    public Task<MesResult> LoginAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(MesResult.Success("奕东 EAP 接口不需要独立登录"));
    }

    public async Task<MesResult> CheckBeforeTestAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateBaseOptions(options);
        if (validation is not null)
        {
            return validation;
        }

        if (string.IsNullOrWhiteSpace(request.Barcode))
        {
            return MesResult.Fail("奕东 EAP 卡关失败：条码不能为空");
        }

        var payload = new
        {
            Header = CreateHeader(options, "TIMS_CHECK_LOTCARD_CARRIER_SN_STATION"),
            Body = new
            {
                data = new[] { CreatePreCheckData(options, request.Barcode) }
            }
        };

        return await PostAsync(options, payload, "奕东 EAP 过站校验", cancellationToken);
    }

    public async Task<MesResult> UploadResultAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken = default)
    {
        var validation = ValidateBaseOptions(options);
        if (validation is not null)
        {
            return validation;
        }

        // 不扫码模式没有可供 EAP 过站的真实 SN。此时仅上报治具使用次数，
        // 且不调用 TIMS_LOTCARD_CARRIER_SN_STATION 过站接口。
        if (IsYes(options.Get("NoBarcodeMode", "N")))
        {
            var fixtureValidation = ValidateFixtureCountOptions(options);
            return fixtureValidation ?? await UploadFixtureCountAsync(options, cancellationToken);
        }

        var status = IsPass(request.Result) ? "OK" : "NG";
        var payload = new
        {
            Header = CreateHeader(options, "TIMS_LOTCARD_CARRIER_SN_STATION"),
            Body = new
            {
                data = new[] { CreateStationReportData(options, request.Barcode, status) }
            }
        };

        var stationResult = await PostAsync(options, payload, "奕东 EAP 过站/报工", cancellationToken);
        if (!stationResult.IsSuccess || !IsYes(options.Get("EnableFixtureCount", "N")))
        {
            return stationResult;
        }

        var countResult = await UploadFixtureCountAsync(options, cancellationToken);
        return countResult.IsSuccess
            ? MesResult.Success($"{stationResult.Message}；{countResult.Message}", rawResponse: stationResult.RawResponse)
            : MesResult.Success($"{stationResult.Message}；治具计数未上报：{countResult.Message}", "PARTIAL", stationResult.RawResponse, countResult.Diagnostics);
    }

    private static object CreateHeader(MesPluginOptions options, string interfaceType)
    {
        return new
        {
            sMesUser = options.Get("CustomerCode", "JXYD"),
            sLanguage = options.Get("Language", "S"),
            sFactoryNo = options.Get("FactoryNo", "0"),
            sUserNo = options.Get("ApiUser", "sa"),
            sClientIp = options.Get("ClientIp", options.Get("IpAddress")),
            sInterfaceType = interfaceType
        };
    }

    private static object CreatePreCheckData(MesPluginOptions options, string barcode)
    {
        return new
        {
            SN = barcode,
            TYPE = options.Get("BarcodeType", "SN"),
            CARRIER_NO = options.Get("CarrierNo"),
            LOTCARD_NO = options.Get("LotCardNo"),
            EQUIPMENT_NO = options.Get("FixtureNo", options.Get("Tooling")),
            TERMINAL_NO = options.Get("TerminalNo", options.Get("Station")),
            STATUS = "OK",
            EQUIPCODE = options.Get("EquipmentCode"),
            PRO_NAME = options.Get("ProgramName"),
            PRO_UPDATE_DATE = options.Get("ProgramUpdateDate"),
            PRESSURE_VALUE = options.Get("PressureValue"),
            PRESSING_DATE = options.Get("PressingDate"),
            TEMPERATURE = options.Get("Temperature"),
            IS_CHECK_FEED_MATERIAL = options.Get("CheckFeedMaterial", "N"),
            IS_CHECK_STATION = options.Get("CheckStation", "Y")
        };
    }

    private static object CreateStationReportData(MesPluginOptions options, string barcode, string status)
    {
        return new
        {
            SN = barcode,
            TYPE = options.Get("BarcodeType", "SN"),
            CARRIER_NO = options.Get("CarrierNo"),
            LOTCARD_NO = options.Get("LotCardNo"),
            EQUIPMENT_NO = options.Get("FixtureNo", options.Get("Tooling")),
            TERMINAL_NO = options.Get("TerminalNo", options.Get("Station")),
            STATUS = status,
            EQUIPCODE = options.Get("EquipmentCode"),
            PRO_NAME = options.Get("ProgramName"),
            PRO_UPDATE_DATE = options.Get("ProgramUpdateDate"),
            PRESSURE_VALUE = options.Get("PressureValue"),
            PRESSING_DATE = options.Get("PressingDate"),
            TEMPERATURE = options.Get("Temperature"),
            IS_DEDUCT = options.Get("DeductMaterial", "N"),
            Detail = ParseMaterialDetails(options.Get("MaterialDetails"))
        };
    }

    private static object[] ParseMaterialDetails(string value)
    {
        return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split(':', 2, StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]) && !string.IsNullOrWhiteSpace(parts[1]))
            .Select(parts => (object)new { FID = parts[0], QTY = parts[1] })
            .ToArray();
    }

    private static async Task<MesResult> PostAsync(MesPluginOptions options, object payload, string operation, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(options);
            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(BuildEndpoint(options), content, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return MesResult.Fail($"{operation}失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}", ((int)response.StatusCode).ToString(), raw);
            }

            if (!TryReadResult(raw, out var code, out var message, out var diagnostics))
            {
                return MesResult.Fail($"{operation}失败：EAP 返回格式无效", rawResponse: raw, diagnostics: diagnostics);
            }

            return code == "0"
                ? MesResult.Success($"{operation}成功：{message}", code, raw, diagnostics)
                : MesResult.Fail($"{operation}失败：{message}", code, raw, diagnostics);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"{operation}异常：{ex.Message}");
        }
    }

    private static bool TryReadResult(string raw, out string code, out string message, out string diagnostics)
    {
        code = string.Empty;
        message = string.Empty;
        diagnostics = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (!TryGetProperty(root, "code", out var codeValue))
            {
                diagnostics = "响应缺少 code 字段";
                return false;
            }

            code = codeValue.ToString();
            message = TryGetProperty(root, "message", out var messageValue) ? messageValue.ToString() : string.Empty;
            diagnostics = TryGetProperty(root, "data", out var dataValue) ? dataValue.ToString() : string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            diagnostics = ex.Message;
            return false;
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static MesResult? ValidateBaseOptions(MesPluginOptions options)
    {
        if (!Uri.TryCreate(BuildEndpoint(options), UriKind.Absolute, out _))
        {
            return MesResult.Fail("奕东 EAP 接口地址无效，请配置 Endpoint");
        }

        foreach (var key in new[] { "LotCardNo", "CustomerCode", "ApiUser" })
        {
            if (string.IsNullOrWhiteSpace(options.Get(key)))
            {
                return MesResult.Fail($"奕东 EAP 配置缺少：{key}");
            }
        }

        if (string.IsNullOrWhiteSpace(options.Get("TerminalNo", options.Get("Station"))))
        {
            return MesResult.Fail("奕东 EAP 配置缺少：TerminalNo（或基础工位编号）");
        }

        if (IsYes(options.Get("EnableFixtureCount", "N")) && string.IsNullOrWhiteSpace(options.Get("FixtureNo", options.Get("Tooling"))))
        {
            return MesResult.Fail("启用治具累计次数上报时必须配置 FixtureNo");
        }

        return null;
    }

    private static MesResult? ValidateFixtureCountOptions(MesPluginOptions options)
    {
        return string.IsNullOrWhiteSpace(options.Get("FixtureNo", options.Get("Tooling")))
            ? MesResult.Fail("奕东 EAP 治具信息上传需要配置 FixtureNo（或基础治具编号）")
            : null;
    }

    private static Task<MesResult> UploadFixtureCountAsync(MesPluginOptions options, CancellationToken cancellationToken)
    {
        var countPayload = new
        {
            Header = CreateHeader(options, "TIMS_RECEIVE_MOLD_FIXTRUE_COUNT"),
            Body = new
            {
                data = new[]
                {
                    new
                    {
                        EQUIPCODE = options.Get("EquipmentCode"),
                        EQUIPMENT_NO = options.Get("FixtureNo", options.Get("Tooling")),
                        TEST_COUNT = options.Get("FixtureCount", "1")
                    }
                }
            }
        };

        return PostAsync(options, countPayload, "奕东 EAP 治具累计次数", cancellationToken);
    }

    private static string BuildEndpoint(MesPluginOptions options)
    {
        var endpoint = options.Get("Endpoint", options.Get("BaseUrl")).Trim();
        if (endpoint.EndsWith("TimsDataTrans", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        return endpoint.TrimEnd('/') + DataTransferPath;
    }

    private static HttpClient CreateClient(MesPluginOptions options)
    {
        var timeout = int.TryParse(options.Get("TimeoutSeconds", "5"), out var seconds) ? seconds : 5;
        return new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeout)) };
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

    private static bool IsPass(string result)
    {
        return string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsYes(string value)
    {
        return string.Equals(value, "Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "YES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "TRUE", StringComparison.OrdinalIgnoreCase)
            || value == "1";
    }
}
