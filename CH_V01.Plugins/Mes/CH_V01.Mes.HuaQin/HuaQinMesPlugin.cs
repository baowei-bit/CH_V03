using System.Text.Json;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CH_V01.Core.Mes;

namespace CH_V01.Mes.HuaQin;

/// <summary>
/// 江西弘信 OA 测试数据上传接口。支持协议中的 ICT（SNICTUpload）和功能测试（FunctionalTestUpload）。
/// </summary>
public sealed class HuaQinMesPlugin : IMesPlugin
{
    private const string DefaultEndpoint = "http://172.18.85.30:8090/UploadTestDataWebService.asmx";
    private static readonly string[] IctItemKeys =
    [
        "resistance1", "deviation1", "noise1", "resistance2", "deviation2", "noise2",
        "resistance3", "resistance4", "resistance5", "resistance6", "resistance7",
        "resistance8", "resistance9", "resistance10"
    ];

    private static readonly string[] FunctionalItemKeys =
    [
        "PSDAT", "DATA", "DATA1", "DATA2", "DATA3", "DATA4", "DATA5", "DATA6", "DATA7", "DATA8", "DATA9",
        "YDATA", "YDATA1", "YDATA2", "YDATA3", "YDATA4", "YDATA5", "YDATA6", "YDATA7", "YDATA8", "YDATA9"
    ];

    private static readonly IReadOnlyDictionary<string, string> MesCharacteristicCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["OPEN"] = "ICT_OPEN",
        ["SHORT"] = "ICT_SHORT",
        ["IC引脚压降"] = "ICT_ICYJJY",
        ["IC功能测试"] = "ICT_ICGNCS",
        ["MIC漏音测试"] = "ICT_MICLYCS",
        ["钢片测试"] = "ICT_GPCS",
        ["电阻"] = "ICT_DZ",
        ["电容"] = "ICT_DR",
        ["热敏电阻"] = "ICT_RMDZ",
        ["麦克风灵敏度"] = "ICT_MKFLMD"
    };

    private static readonly IReadOnlyDictionary<string, string> IctTestCategories = CreateCategoryMap(
        ("OPEN", ["J1101_8&J1101_6", "J1101_8&J1101_9", "J1101_8&J1101_15", "J1101_8&Conductive Foam", "J1101_7&T1101", "J1101_10&T1102", "J1101_P1&T1106", "J1101_P2&T1107"]),
        ("SHORT", ["SHORT"]),
        ("IC引脚压降", ["G-SCL", "G-VDD-BYP", "G-FORCE-INT", "G-MIC-OUT", "G-MIC-VDD", "G-SWIPE-INT", "G-1V8-VDD", "G-SDA"]),
        ("钢片测试", ["SUS1", "SUS2"]),
        ("电阻", ["R1108", "R1109"]),
        ("电容", ["C1352D1101", "C1103", "C1101C1109C1111C1112"]),
        ("热敏电阻", ["Ref-Temp", "RT1101", "RT1101-Temp", "RT1101-Ref-TDiff"]),
        ("麦克风灵敏度", ["SPC_Mic_Audio_Test"]));

    private static readonly IReadOnlyDictionary<string, string> FctTestCategories = CreateCategoryMap(
        ("SHORT", ["SHORT"]),
        ("IC引脚压降", ["G-SCL", "G-VDD-BYP", "G-FORCE-INT", "G-MIC-OUT", "G-MIC-VDD", "G-SWIPE-INT", "G-1V8-VDD", "G-SDA"]),
        ("IC功能测试", ["U102-ID", "U102-INTL", "U102-INTH", "U101-ID", "U101-PS-NEAR", "U101-PS-FAR", "U104-ID", "U104-REG00", "U104-REG01", "U104-ADCOUT1", "U104-ADCOUT2", "U104-ADCOUT3", "U104-ADCOUT4", "U104-ADCOUT5", "U104-ADCOUT6", "U104-ADCOUT7", "U104-ADCOUT8", "U104-ADCOUT9", "U104-ADCOUT10", "U104-ADCOUT-MAX", "U104-ADCOUT-MIN", "U104-ADCOUT-AVG", "U104-ADCOUT-Stdev", "P1", "P0", "NOForce0-ADCOUT1", "NOForce0-ADCOUT2", "NOForce0-ADCOUT3", "NOForce0-ADCOUT4", "NOForce0-ADCOUT5", "NOForce0-ADCOUT6", "NOForce0-ADCOUT7", "NOForce0-ADCOUT8", "NOForce0-ADCOUT9", "NOForce0-ADCOUT10", "NOForce0-ADCOUT-MAX", "NOForce0-ADCOUT-MIN", "NOForce0-ADCOUT-AVG", "NOForce0-ADCOUT-Stdev", "Force-ADCOUT1", "Force-ADCOUT2", "Force-ADCOUT3", "Force-ADCOUT4", "Force-ADCOUT5", "Force-ADCOUT6", "Force-ADCOUT7", "Force-ADCOUT8", "Force-ADCOUT9", "Force-ADCOUT10", "Force-ADCOUT-MAX", "Force-ADCOUT-MIN", "Force-ADCOUT-AVG", "Force-ADCOUT-Stdev", "NOForce-ADCOUT1", "NOForce-ADCOUT2", "NOForce-ADCOUT3", "NOForce-ADCOUT4", "NOForce-ADCOUT5", "NOForce-ADCOUT6", "NOForce-ADCOUT7", "NOForce-ADCOUT8", "NOForce-ADCOUT9", "NOForce-ADCOUT10", "NOForce-ADCOUT-MAX", "NOForce-ADCOUT-MIN", "NOForce-ADCOUT-AVG", "NOForce-ADCOUT-Stdev", "Baseline", "Raw_0", "Raw_5g", "Raw_recover", "Signal_5g", "Sensitivity_norm", "Drift_r0", "U104-ADCOUT", "U104-10samp ident", "U104-Clip1", "U104-Clip2", "U104-Clip3", "U104-Clip4", "U104-Clip5", "U104-Clip6", "U104-Clip7", "U104-Clip8", "U104-Clip9", "U104-Clip10", "U104-Clip10-MAX", "U104-Clip10-MIN", "U104-Clip10-AVG", "U104-Clip10-Stdev", "INT-STATEA", "INT-STATEB", "INT-STATEC"]),
        ("MIC漏音测试", ["MIC_100hz_Sealing", "MIC_300hz_Sealing", "MIC_500hz_Sealing", "MIC_1000hz_Sealing", "MIC_2000hz_Sealing"]));

    public string Id => "HongXin.OA";

    public string Name => "HongXin OA";

    public MesPluginCapabilities Capabilities { get; } = new()
    {
        SupportsConnectionTest = true,
        SupportsResultUpload = true,
        SupportsOfflineCache = true
    };

    public IReadOnlyList<MesPluginConfigField> GetConfigFields() =>
    [
        Field("Endpoint", "OA 接口地址", DefaultEndpoint, true),
        Field("UploadType", "上传类型（ICT/FCT）", "ICT", true),
        Field("MaterialNo", "物料编号 elno", "", true),
        Field("DeviceNo", "设备号（ICT: Index；FCT: ID）", "", true),
        Field("ItemId", "ICT 料号 Itemid（可选）"),
        Field("BatchNo", "FCT 批次号 Result（可选）"),
        Field("TimeoutSeconds", "超时秒数", "10", true),
        Choice("MesUploadMode", "MES 上传模式", "Capacity", "Capacity", "Station"),
        Field("MesBasicAuthUser", "MES Basic Auth 账号", "HQ001", true),
        Field("MesBasicAuthPassword", "MES Basic Auth 密码", "HQ001", true, true),
        Field("MesTerminalMac", "MES 终端 MACIP", "", true),
        Field("MesTerminalName", "MES 终端代号 maname（可选）"),
        Field("MesOperatorId", "MES 作业人员 user_id", "", true),
        Field("MesStartScStatus", "开工流程状态 scStatus（可选）"),
        Field("MesStartWi09Ruid", "开工站点 ID wi09Ruid（可选）"),
        Field("MesStartScStno", "开工站点代号 scStno（可选）"),
        Field("MesStartScRcno", "开工流程单号 scRcno"),
        Field("MesStartPtNo", "开工工序代号 ptNo", "", true),
        Field("MesStartVcNo", "开工标识卡 vcNo（可选）"),
        Field("MesCapacityDevNo", "产能设备代号 dev_no", "", true),
        Field("MesCapacityPtNo", "产能工序编号 pt_no（可选）"),
        Field("MesCapacityScRcno", "产能流程单号 sc_rcno（可选）"),
        Field("MesCapacityScOdno", "产能生产工单 sc_odno（可选）"),
        Field("MesCapacityElNo", "产能物料编号 el_no（可选）"),
        Field("MesCapacityElVer", "产能物料版本 el_ver（可选）"),
        Field("MesCapacityExpired", "产能历史数据标记 expired（可选）"),
        Field("MesCapacityPrTag", "产能产品位号 pr_tag（可选）"),
        Field("MesCapacityIsFinish", "产能流程结束 is_finish（可选）"),
        Field("MesCapacityProduction", "产能生产数量 production（可选）"),
        Field("MesCapacityLinkFile", "产能跨设备文件标记 linkfile（可选）"),
        Field("MesCapacityBcNo", "产能班次 bc_no（可选）"),
        Field("MesStationDevNo", "过站设备代号 devNo", "", true),
        Field("MesStationWi09Ruid", "过站站点 ID wi09Ruid（可选）"),
        Field("MesStationScRcno", "过站流程单号 scRcno（可选）"),
        Field("MesStationScOdno", "过站生产工单 scOdno（可选）"),
        Field("MesStationScPrno", "过站工作中心 scPrno（可选）"),
        Field("MesStationVcNo", "过站标识卡 vcNo（可选）"),
        Field("MesCharacteristicCodeMap", "特性代号映射（类别=MES代号;，可选）"),
        Field("MesDefectCodeMap", "不良代号映射（项目=MES代号;）")
    ];

    public Task<MesResult> TestConnectionAsync(MesPluginOptions options, CancellationToken cancellationToken = default)
    {
        var validation = ValidateOptions(options);
        return Task.FromResult(validation ?? MesResult.Success("HongXin OA 配置有效；接口未定义独立连通性检测，实际上传时验证网络与服务。"));
    }

    public Task<MesResult> LoginAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(MesResult.Success("HongXin OA 接口不需要登录。"));

    public Task<MesResult> CheckBeforeTestAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default) =>
        Task.FromResult(MesResult.Success("HongXin OA 协议未定义测试前卡关，已跳过。"));

    public async Task<MesResult> UploadResultAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken = default)
    {
        var oaResult = await UploadOriginalOaAsync(options, request, cancellationToken);
        var mesResult = await UploadNewMesAsync(options, request, cancellationToken);
        var diagnostics = JoinDiagnostics("原 OA", oaResult.Diagnostics, "新 MES", mesResult.Diagnostics);
        var rawResponse = JoinDiagnostics("原 OA", oaResult.RawResponse, "新 MES", mesResult.RawResponse);

        if (oaResult.IsSuccess && mesResult.IsSuccess)
        {
            return MesResult.Success($"HongXin 原 OA 与 MES 上传成功：{mesResult.Message}", rawResponse: rawResponse, diagnostics: diagnostics);
        }

        return MesResult.Fail($"HongXin 上传失败：原 OA：{oaResult.Message}；MES：{mesResult.Message}", rawResponse: rawResponse, diagnostics: diagnostics);
    }

    private async Task<MesResult> UploadOriginalOaAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateOptions(options);
        if (validation is not null)
        {
            return validation;
        }

        if (string.IsNullOrWhiteSpace(request.Barcode))
        {
            return MesResult.Fail("HongXin OA 上传失败：条码不能为空。");
        }

        var isIct = IsIct(options);
        var method = isIct ? "SNICTUpload" : "FunctionalTestUpload";
        var payload = isIct ? BuildIctPayload(options, request) : BuildFunctionalPayload(options, request);
        var endpoint = BuildMethodEndpoint(options.Get("Endpoint", options.Get("BaseUrl", DefaultEndpoint)), method);
        var body = JsonSerializer.Serialize(payload);
        var diagnostics = $"请求：POST {endpoint}\n请求头：Content-Type=application/x-www-form-urlencoded\n请求体：JSON={body}";

        try
        {
            using var client = CreateClient(options);
            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("JSON", body)
            ]);
            using var response = await client.PostAsync(endpoint, content, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            diagnostics += $"\n响应：HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n响应体：{raw}";

            if (!response.IsSuccessStatusCode)
            {
                return MesResult.Fail($"HongXin OA {method} 上传失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}", ((int)response.StatusCode).ToString(), raw, diagnostics);
            }

            if (!TryReadBusinessResult(raw, out var success, out var message, out var parseError))
            {
                return MesResult.Fail($"HongXin OA {method} 上传失败：返回格式无效。", rawResponse: raw, diagnostics: diagnostics + $"\n解析错误：{parseError}");
            }

            return success
                ? MesResult.Success($"HongXin OA {method} 上传成功{(string.IsNullOrWhiteSpace(message) ? string.Empty : $"：{message}")}", rawResponse: raw, diagnostics: diagnostics)
                : MesResult.Fail($"HongXin OA {method} 上传失败{(string.IsNullOrWhiteSpace(message) ? string.Empty : $"：{message}")}", rawResponse: raw, diagnostics: diagnostics);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"HongXin OA {method} 上传异常：{ex.Message}", diagnostics: diagnostics);
        }
    }

    private async Task<MesResult> UploadNewMesAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken)
    {
        var validation = ValidateNewMesOptions(options, request);
        if (validation is not null)
        {
            return validation;
        }

        var startResult = await StartWorkOrderAsync(options, request, cancellationToken);
        if (!startResult.IsSuccess)
        {
            return startResult;
        }

        var uploadResult = IsCapacityMode(options)
            ? await UploadCapacityAsync(options, request, cancellationToken)
            : await UploadStationAsync(options, request, cancellationToken);
        return new MesResult
        {
            IsSuccess = uploadResult.IsSuccess,
            Code = uploadResult.Code,
            Message = uploadResult.Message,
            Diagnostics = JoinDiagnostics("流程单开工", startResult.Diagnostics, "MES 上传", uploadResult.Diagnostics),
            RawResponse = JoinDiagnostics("流程单开工", startResult.RawResponse, "MES 上传", uploadResult.RawResponse)
        };
    }

    private static MesResult? ValidateNewMesOptions(MesPluginOptions options, MesUploadRequest request)
    {
        foreach (var key in new[] { "MesBasicAuthUser", "MesBasicAuthPassword", "MesTerminalMac", "MesOperatorId", "MesStartPtNo" })
        {
            if (string.IsNullOrWhiteSpace(options.Get(key)))
            {
                return MesResult.Fail($"HongXin MES 配置缺少：{key}。", diagnostics: "新 MES 上传未发起。");
            }
        }

        var deviceKey = IsCapacityMode(options) ? "MesCapacityDevNo" : "MesStationDevNo";
        if (string.IsNullOrWhiteSpace(options.Get(deviceKey)))
        {
            return MesResult.Fail($"HongXin MES 配置缺少：{deviceKey}。", diagnostics: "新 MES 上传未发起。");
        }

        if (request.Files.Count == 0)
        {
            return MesResult.Fail("HongXin MES 上传失败：未找到本次测试生成的 CSV 文件。", diagnostics: "新 MES 上传未发起。");
        }

        if (BuildCharacteristicItems(options, request).Count == 0)
        {
            return MesResult.Fail("HongXin MES 上传失败：本次测试项未匹配 FCT/ICT 测试分类表。", diagnostics: "新 MES 上传未发起。");
        }

        return null;
    }

    private static async Task<MesResult> StartWorkOrderAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken)
    {
        const string endpoint = "http://172.18.85.44:8080/honsoft/honwi09/crossingStnoByScrno.ws";
        var payload = new Dictionary<string, object?>();
        AddIfPresent(payload, "scStatus", options.Get("MesStartScStatus"));
        AddIfPresent(payload, "wi09Ruid", options.Get("MesStartWi09Ruid"));
        AddIfPresent(payload, "scStno", options.Get("MesStartScStno"));
        AddIfPresent(payload, "scRcno", options.Get("MesStartScRcno", request.ProdNo));
        AddIfPresent(payload, "ptNo", options.Get("MesStartPtNo"));
        AddIfPresent(payload, "vcNo", options.Get("MesStartVcNo"));
        return await PostJsonAsync(options, endpoint, payload, requireCode200: true, "HongXin MES 流程单开工", cancellationToken);
    }

    private static async Task<MesResult> UploadCapacityAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken)
    {
        const string endpoint = "http://172.18.85.24:9001/kafkaProducer";
        var payload = new Dictionary<string, object?>
        {
            ["dev_no"] = options.Get("MesCapacityDevNo"),
            ["el_sno"] = request.Barcode,
            ["pt_pqyn"] = ToMesResult(request.Result),
            ["file"] = BuildFiles(request.Files),
            ["sc_dlqty"] = 1,
            ["wi_date"] = FormatTime(request.EndTime),
            ["sp_noList"] = BuildCharacteristicItems(options, request)
        };
        AddIfPresent(payload, "pt_no", options.Get("MesCapacityPtNo"));
        AddIfPresent(payload, "sc_rcno", options.Get("MesCapacityScRcno", request.ProdNo));
        AddIfPresent(payload, "sc_odno", options.Get("MesCapacityScOdno"));
        AddIfPresent(payload, "el_no", options.Get("MesCapacityElNo"));
        AddIfPresent(payload, "el_ver", options.Get("MesCapacityElVer"));
        AddIfPresent(payload, "expired", options.Get("MesCapacityExpired"));
        AddIfPresent(payload, "pr_tag", options.Get("MesCapacityPrTag"));
        AddIfPresent(payload, "is_finish", options.Get("MesCapacityIsFinish"));
        AddIfPresent(payload, "dt_stime", FormatTime(request.StartTime));
        AddIfPresent(payload, "bc_no", options.Get("MesCapacityBcNo"));
        var parameter = BuildParameter(options);
        if (parameter.Count > 0)
        {
            payload["parameter"] = parameter;
        }
        AddFailureCodes(payload, "qc_reason", options, request);

        var json = JsonSerializer.Serialize(payload);
        var diagnostics = $"请求：POST {endpoint}\n请求头：Content-Type=application/x-www-form-urlencoded\n请求体：topic=honsoft.prodata\njson={json}";
        try
        {
            using var client = CreateClient(options);
            using var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("topic", "honsoft.prodata"),
                new KeyValuePair<string, string>("json", json)
            ]);
            using var response = await client.PostAsync(endpoint, content, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            diagnostics += $"\n响应：HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n响应体：{raw}";
            return ReadMesHttpResult(response, raw, "status", 0, "HongXin MES 设备产能上传", diagnostics);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"HongXin MES 设备产能上传异常：{ex.Message}", diagnostics: diagnostics);
        }
    }

    private static async Task<MesResult> UploadStationAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken)
    {
        const string endpoint = "http://172.18.85.44:8080/honsoft/honwi09/crossingStationBySN.ws";
        var payload = new Dictionary<string, object?>
        {
            ["elSno"] = request.Barcode,
            ["devNo"] = options.Get("MesStationDevNo"),
            ["ptPqyn"] = ToMesResult(request.Result),
            ["arrFile"] = BuildFiles(request.Files),
            ["sc_dlqty"] = 1,
            ["dt_stime"] = FormatTime(request.StartTime),
            ["wi_date"] = FormatTime(request.EndTime),
            ["sp_noList"] = BuildCharacteristicItems(options, request)
        };
        AddIfPresent(payload, "wi09Ruid", options.Get("MesStationWi09Ruid"));
        AddIfPresent(payload, "scRcno", options.Get("MesStationScRcno", request.ProdNo));
        AddIfPresent(payload, "scOdno", options.Get("MesStationScOdno"));
        AddIfPresent(payload, "scPrno", options.Get("MesStationScPrno"));
        AddIfPresent(payload, "vcNo", options.Get("MesStationVcNo"));
        AddFailureCodes(payload, "qcReason", options, request);
        return await PostJsonAsync(options, endpoint, payload, requireCode200: true, "HongXin MES 设备单品过站", cancellationToken);
    }

    private static async Task<MesResult> PostJsonAsync(MesPluginOptions options, string endpoint, object payload, bool requireCode200, string action, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload);
        var diagnostics = $"请求：POST {endpoint}\n请求头：Content-Type=application/json；Authorization=Basic（已脱敏）；MACIP={options.Get("MesTerminalMac")}；maname={options.Get("MesTerminalName")}；user_id={options.Get("MesOperatorId")}\n请求体：{json}";
        try
        {
            using var client = CreateClient(options);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            AddMesHeaders(httpRequest, options);
            using var response = await client.SendAsync(httpRequest, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            diagnostics += $"\n响应：HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n响应体：{raw}";
            return ReadMesHttpResult(response, raw, "code", requireCode200 ? 200 : 0, action, diagnostics);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"{action}异常：{ex.Message}", diagnostics: diagnostics);
        }
    }

    private static MesResult ReadMesHttpResult(HttpResponseMessage response, string raw, string resultProperty, int successCode, string action, string diagnostics)
    {
        if (!response.IsSuccessStatusCode)
        {
            return MesResult.Fail($"{action}失败：HTTP {(int)response.StatusCode} {response.ReasonPhrase}", ((int)response.StatusCode).ToString(), raw, diagnostics);
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var message = TryGetProperty(root, "msg", out var msg) ? msg.ToString() :
                TryGetProperty(root, "message", out var messageValue) ? messageValue.ToString() : string.Empty;
            if (!TryGetProperty(root, resultProperty, out var codeValue) || !int.TryParse(codeValue.ToString(), out var code))
            {
                return MesResult.Fail($"{action}失败：返回缺少 {resultProperty}。{message}", rawResponse: raw, diagnostics: diagnostics);
            }

            return code == successCode
                ? MesResult.Success($"{action}成功{(string.IsNullOrWhiteSpace(message) ? string.Empty : $"：{message}")}", code.ToString(), raw, diagnostics)
                : MesResult.Fail($"{action}失败：{message}", code.ToString(), raw, diagnostics);
        }
        catch (JsonException ex)
        {
            return MesResult.Fail($"{action}失败：返回格式无效。", rawResponse: raw, diagnostics: diagnostics + $"\n解析错误：{ex.Message}");
        }
    }

    private static void AddMesHeaders(HttpRequestMessage request, MesPluginOptions options)
    {
        var credential = $"{options.Get("MesBasicAuthUser")}:{options.Get("MesBasicAuthPassword")}";
        request.Headers.TryAddWithoutValidation("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(credential))}");
        request.Headers.TryAddWithoutValidation("MACIP", options.Get("MesTerminalMac"));
        AddHeaderIfPresent(request, "maname", options.Get("MesTerminalName"));
        request.Headers.TryAddWithoutValidation("user_id", options.Get("MesOperatorId"));
    }

    private static void AddHeaderIfPresent(HttpRequestMessage request, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            request.Headers.TryAddWithoutValidation(key, value);
        }
    }

    private static bool IsCapacityMode(MesPluginOptions options) =>
        !string.Equals(options.Get("MesUploadMode", "Capacity"), "Station", StringComparison.OrdinalIgnoreCase);

    private static string ToMesResult(string result) =>
        string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase) || string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase)
            ? "Y"
            : "N";

    private static List<Dictionary<string, object?>> BuildFiles(IReadOnlyList<MesUploadFile> files)
    {
        return files
            .Where(file => !string.IsNullOrWhiteSpace(file.FilePath) && File.Exists(file.FilePath))
            .Select(file => new Dictionary<string, object?>
            {
                ["file_data"] = Convert.ToBase64String(File.ReadAllBytes(file.FilePath)),
                ["file_name"] = Path.GetFileName(file.FilePath),
                ["load_path"] = file.FilePath
            })
            .ToList();
    }

    private static List<Dictionary<string, object?>> BuildCharacteristicItems(MesPluginOptions options, MesUploadRequest request)
    {
        var codes = ParseMap(options.Get("MesCharacteristicCodeMap"));
        var categoryMap = IsIct(options) ? IctTestCategories : FctTestCategories;
        return request.TestItems
            .Where(item => categoryMap.ContainsKey(item.Name))
            .GroupBy(item => categoryMap[item.Name], StringComparer.OrdinalIgnoreCase)
            .Select(group =>
        {
            var payload = new Dictionary<string, object?>
            {
                ["sp_no"] = codes.TryGetValue(group.Key, out var code)
                    ? code
                    : MesCharacteristicCodes.TryGetValue(group.Key, out var defaultCode) ? defaultCode : group.Key,
                ["value"] = group.All(item => IsTestItemPassed(item)) ? "Y" : "N",
                ["sp_require"] = JsonSerializer.Serialize(group.Select(item => new
                {
                    name = item.Name,
                    value = item.Value,
                    result = item.Result,
                    upperLimit = item.UpperLimit,
                    lowerLimit = item.LowerLimit,
                    unit = item.Unit
                })),
                ["detail"] = new List<object>()
            };
            return payload;
        }).ToList();
    }

    private static bool IsTestItemPassed(MesTestItem item) =>
        string.Equals(item.Result, "OK", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.Result, "PASS", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(item.Result, "Y", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> CreateCategoryMap(params (string Category, string[] Items)[] groups)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, items) in groups)
        {
            foreach (var item in items)
            {
                map[item] = category;
            }
        }

        return map;
    }

    private static Dictionary<string, string> BuildParameter(MesPluginOptions options)
    {
        var parameter = new Dictionary<string, string>();
        AddIfPresent(parameter, "production", options.Get("MesCapacityProduction"));
        AddIfPresent(parameter, "linkfile", options.Get("MesCapacityLinkFile"));
        return parameter;
    }

    private static void AddFailureCodes(IDictionary<string, object?> payload, string key, MesPluginOptions options, MesUploadRequest request)
    {
        if (ToMesResult(request.Result) == "Y")
        {
            return;
        }

        var map = ParseMap(options.Get("MesDefectCodeMap"));
        var codes = request.TestItems
            .Where(item => !string.Equals(item.Result, "OK", StringComparison.OrdinalIgnoreCase) &&
                           !string.Equals(item.Result, "PASS", StringComparison.OrdinalIgnoreCase))
            .Select(item => map.TryGetValue(item.Name, out var code) ? code : item.Name)
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(code => code, _ => "N", StringComparer.OrdinalIgnoreCase);
        if (codes.Count > 0)
        {
            payload[key] = codes;
        }
    }

    private static Dictionary<string, string> ParseMap(string value)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && !string.IsNullOrWhiteSpace(pair[0]) && !string.IsNullOrWhiteSpace(pair[1]))
            {
                map[pair[0]] = pair[1];
            }
        }

        return map;
    }

    private static string JoinDiagnostics(string firstName, string first, string secondName, string second)
    {
        var entries = new List<string>();
        if (!string.IsNullOrWhiteSpace(first)) entries.Add($"[{firstName}]\n{first}");
        if (!string.IsNullOrWhiteSpace(second)) entries.Add($"[{secondName}]\n{second}");
        return string.Join("\n\n", entries);
    }

    private static Dictionary<string, string> BuildIctPayload(MesPluginOptions options, MesUploadRequest request)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Barcode"] = request.Barcode,
            ["ISOK"] = NormalizeResult(request.Result),
            ["Index"] = options.Get("DeviceNo"),
            ["elno"] = options.Get("MaterialNo"),
            ["CreateTime"] = FormatTime(request.EndTime)
        };

        AddIfPresent(payload, "Itemid", options.Get("ItemId"));
        AddTestItems(payload, IctItemKeys, request.TestItems);
        return payload;
    }

    private static Dictionary<string, string> BuildFunctionalPayload(MesPluginOptions options, MesUploadRequest request)
    {
        var payload = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Barcode"] = request.Barcode,
            ["elno"] = options.Get("MaterialNo"),
            ["TestResult"] = NormalizeResult(request.Result),
            ["ID"] = options.Get("DeviceNo")
        };

        AddTestItems(payload, FunctionalItemKeys, request.TestItems);
        AddIfPresent(payload, "TestTime", FormatTime(request.EndTime));
        AddIfPresent(payload, "Result", options.Get("BatchNo", request.PackNo));
        return payload;
    }

    private static void AddTestItems(IDictionary<string, string> payload, IReadOnlyList<string> keys, IReadOnlyList<MesTestItem> items)
    {
        for (var index = 0; index < Math.Min(keys.Count, items.Count); index++)
        {
            var value = items[index].Value?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                payload[keys[index]] = value;
            }
        }
    }

    private static void AddIfPresent(IDictionary<string, string> payload, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            payload[key] = value;
        }
    }

    private static void AddIfPresent(IDictionary<string, object?> payload, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            payload[key] = value;
        }
    }

    private static MesResult? ValidateOptions(MesPluginOptions options)
    {
        if (!Uri.TryCreate(options.Get("Endpoint", options.Get("BaseUrl", DefaultEndpoint)), UriKind.Absolute, out _))
        {
            return MesResult.Fail("HongXin OA 配置错误：Endpoint 必须是有效的绝对地址。");
        }

        if (!string.Equals(options.Get("UploadType", "ICT"), "ICT", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(options.Get("UploadType"), "FCT", StringComparison.OrdinalIgnoreCase))
        {
            return MesResult.Fail("HongXin OA 配置错误：UploadType 仅支持 ICT 或 FCT。");
        }

        foreach (var key in new[] { "MaterialNo", "DeviceNo" })
        {
            if (string.IsNullOrWhiteSpace(options.Get(key)))
            {
                return MesResult.Fail($"HongXin OA 配置缺少：{key}。");
            }
        }

        return null;
    }

    private static bool IsIct(MesPluginOptions options) =>
        string.Equals(options.Get("UploadType", "ICT"), "ICT", StringComparison.OrdinalIgnoreCase);

    private static string BuildMethodEndpoint(string endpoint, string method) =>
        endpoint.TrimEnd('/') + "/" + method;

    private static HttpClient CreateClient(MesPluginOptions options)
    {
        var timeout = int.TryParse(options.Get("TimeoutSeconds", "10"), out var seconds) ? seconds : 10;
        return new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeout)) };
    }

    private static string NormalizeResult(string result) =>
        string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result, "TRUE", StringComparison.OrdinalIgnoreCase)
            ? "PASS"
            : "False";

    private static string FormatTime(DateTime value) =>
        (value == default ? DateTime.Now : value).ToString("yyyy/MM/dd HH:mm:ss");

    private static bool TryReadBusinessResult(string raw, out bool success, out string message, out string error)
    {
        success = false;
        message = string.Empty;
        error = string.Empty;
        try
        {
            var json = UnwrapXmlResponse(raw);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (TryGetProperty(root, "d", out var wrapped))
            {
                root = wrapped.ValueKind == JsonValueKind.String
                    ? JsonDocument.Parse(wrapped.GetString() ?? string.Empty).RootElement.Clone()
                    : wrapped;
            }

            if (!TryGetProperty(root, "IsSuccess", out var result))
            {
                error = "响应缺少 IsSuccess 字段。";
                return false;
            }

            success = result.ValueKind == JsonValueKind.True ||
                (result.ValueKind != JsonValueKind.False && bool.TryParse(result.ToString(), out var parsed) && parsed);
            message = TryGetProperty(root, "MSG", out var msg) ? msg.ToString() : string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or XmlException or InvalidOperationException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string UnwrapXmlResponse(string raw)
    {
        var content = raw.Trim();
        if (!content.StartsWith('<'))
        {
            return content;
        }

        var root = XDocument.Parse(content).Root;
        if (root is null)
        {
            throw new XmlException("响应 XML 缺少根节点。");
        }

        return root.Value.Trim();
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

    private static MesPluginConfigField Field(string key, string displayName, string defaultValue = "", bool required = false, bool secret = false) => new()
    {
        Key = key,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        IsRequired = required,
        IsSecret = secret
    };

    private static MesPluginConfigField Choice(string key, string displayName, string defaultValue, params string[] choices) => new()
    {
        Key = key,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        IsRequired = true,
        Choices = choices
    };
}
