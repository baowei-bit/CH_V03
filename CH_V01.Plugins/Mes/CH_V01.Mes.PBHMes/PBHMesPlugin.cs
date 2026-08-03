using System.Net.Http.Json;
using System.Text.Json;
using CH_V01.Core.Mes;

namespace CH_V01.Mes.PBHMes;

/// <summary>
/// 鹏博辉深圳原 ICT 程序所使用的 SwiftMOM HTTP MES 协议适配器。
/// </summary>
public sealed class PBHMesPlugin : IMesPlugin
{
    public string Id => "PBHMes";

    public string Name => "PBHMes（鹏博辉 MES）";

    public MesPluginCapabilities Capabilities { get; } = new()
    {
        SupportsConnectionTest = true,
        SupportsLogin = true,
        SupportsPreCheck = true,
        SupportsResultUpload = true,
        SupportsOfflineCache = true
    };

    public IReadOnlyList<MesPluginConfigField> GetConfigFields() =>
    [
        Field("BaseUrl", "MES API 地址", "http://127.0.0.1:8080/swiftmom/api/", true),
        Field("TimeoutSeconds", "超时秒数", "5", true),
        Field("Retest", "复测标志", ""),
        Field("Weight", "重量", ""),
        Field("Remark", "上传备注", ""),
        Field("Rmk1", "备注字段 1", ""),
        Field("Rmk2", "备注字段 2", ""),
        Field("Rmk3", "备注字段 3", ""),
        Field("Rmk4", "备注字段 4", "")
    ];

    public Task<MesResult> TestConnectionAsync(MesPluginOptions options, CancellationToken cancellationToken = default) =>
        LoginAsync(options, new MesCheckRequest
        {
            UserNo = options.Get("UserNo"),
            Password = options.Get("Password")
        }, cancellationToken);

    public async Task<MesResult> LoginAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options);
        var result = await PostAsync(client, GetBaseUrl(options), "clientLogin", new
        {
            userNo = request.UserNo,
            password = request.Password
        }, string.Empty, cancellationToken);

        if (!result.IsSuccess)
        {
            return result;
        }

        var token = ReadString(result.RawResponse, "token");
        return string.IsNullOrWhiteSpace(token)
            ? MesResult.Fail("clientLogin 成功但未返回 token", result.Code, result.RawResponse)
            : MesResult.Success(result.Message, result.Code, token, result.RawResponse);
    }

    public async Task<MesResult> CheckBeforeTestAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        var login = await LoginAsync(options, request, cancellationToken);
        if (!login.IsSuccess)
        {
            return login;
        }

        using var client = CreateClient(options);
        var result = await PostAsync(client, GetBaseUrl(options), "checkRoute", new
        {
            pcbSeq = request.Barcode,
            prodNo = request.ProdNo,
            stationNo = request.StationNo,
            retest = request.Retest
        }, login.Token, cancellationToken);

        return result.IsSuccess
            ? MesResult.Success(result.Message, result.Code, login.Token, result.RawResponse, result.Diagnostics)
            : result;
    }

    public async Task<MesResult> UploadResultAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken = default)
    {
        var token = options.Get("Token");
        if (string.IsNullOrWhiteSpace(token))
        {
            var login = await LoginAsync(options, new MesCheckRequest
            {
                UserNo = request.UserNo,
                Password = options.Get("Password")
            }, cancellationToken);
            if (!login.IsSuccess)
            {
                return login;
            }

            token = login.Token;
        }

        // 字段名和拼写与原 WinForm_PY_Ict 的 TestData 完全一致；testItem 本身是 JSON 字符串。
        var testItem = JsonSerializer.Serialize(request.TestItems.Select((item, index) => new
        {
            Sort = (index + 1).ToString(),
            Item = item.Name,
            USL = item.UpperLimit,
            LSL = item.LowerLimit,
            Unit = item.Unit,
            Value = item.Value,
            Raust = NormalizeResult(item.Result)
        }));

        using var client = CreateClient(options);
        return await PostAsync(client, GetBaseUrl(options), "createRoute", new
        {
            pcbSeq = request.Barcode,
            prodNo = request.ProdNo,
            stationNo = request.StationNo,
            result = NormalizeResult(request.Result),
            // 原程序固定上传空备注；如现场需要，可通过插件配置显式填写。
            remark = options.Get("Remark"),
            testItem,
            userNo = request.UserNo,
            weight = options.Get("Weight", request.Weight),
            packNo = request.PackNo,
            rmk1 = options.Get("Rmk1"),
            rmk2 = options.Get("Rmk2"),
            rmk3 = options.Get("Rmk3"),
            rmk4 = options.Get("Rmk4")
        }, token, cancellationToken);
    }

    private static async Task<MesResult> PostAsync(HttpClient client, Uri baseUrl, string endpoint, object payload, string token, CancellationToken cancellationToken)
    {
        var uri = new Uri(baseUrl, endpoint);
        var requestBody = JsonSerializer.Serialize(payload);
        var requestInfo = $"MES 请求：POST {uri}\n请求头：Content-Type=application/json; token={(string.IsNullOrWhiteSpace(token) ? "未携带" : "已携带（已脱敏）")}\n请求体：{requestBody}";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = JsonContent.Create(payload)
            };
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Add("token", token);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var diagnostics = $"{requestInfo}\n响应状态：HTTP {(int)response.StatusCode} {response.ReasonPhrase}\n响应内容：{raw}";
            if (!response.IsSuccessStatusCode)
            {
                return MesResult.Fail($"MES HTTP {(int)response.StatusCode}: {response.ReasonPhrase}", ((int)response.StatusCode).ToString(), raw, diagnostics);
            }

            var code = ReadString(raw, "msgId");
            var message = ReadString(raw, "msgStr");
            if (code == "0")
            {
                return MesResult.Success(string.IsNullOrWhiteSpace(message) ? "MES 返回成功" : message, code, ReadString(raw, "token"), raw, diagnostics);
            }

            return MesResult.Fail(string.IsNullOrWhiteSpace(message) ? "MES 返回失败" : message, string.IsNullOrWhiteSpace(code) ? "1" : code, raw, diagnostics);
#if false
            return code == "0"
                ? MesResult.Success(string.IsNullOrWhiteSpace(message) ? "MES 返回成功" : message, code, ReadString(raw, "token"), raw)
                : MesResult.Fail(string.IsNullOrWhiteSpace(message) ? "MES 返回失败" : message, string.IsNullOrWhiteSpace(code) ? "1" : code, raw);
#endif
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"MES 请求异常：{ex.Message}");
        }
    }

    private static MesPluginConfigField Field(string key, string displayName, string defaultValue, bool required = false) => new()
    {
        Key = key,
        DisplayName = displayName,
        DefaultValue = defaultValue,
        IsRequired = required
    };

    private static HttpClient CreateClient(MesPluginOptions options)
    {
        var timeout = int.TryParse(options.Get("TimeoutSeconds", "5"), out var seconds) ? seconds : 5;
        return new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(1, timeout)) };
    }

    private static Uri GetBaseUrl(MesPluginOptions options)
    {
        var value = options.Get("BaseUrl", "http://127.0.0.1:8080/swiftmom/api/");
        return new Uri(value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/", UriKind.Absolute);
    }

    private static string NormalizeResult(string result) =>
        string.Equals(result, "PASS", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result, "Y", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(result, "TRUE", StringComparison.OrdinalIgnoreCase)
            ? "PASS"
            : "FAIL";

    private static string ReadString(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value) ? value.ToString() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}
