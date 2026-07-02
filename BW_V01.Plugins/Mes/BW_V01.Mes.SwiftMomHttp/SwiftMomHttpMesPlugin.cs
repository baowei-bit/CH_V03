using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BW_V01.Core.Mes;

namespace BW_V01.Mes.SwiftMomHttp;

public sealed class SwiftMomHttpMesPlugin : IMesPlugin
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id => "SwiftMom.Http";

    public string Name => "SwiftMOM HTTP MES 插件";

    public MesPluginCapabilities Capabilities { get; } = new()
    {
        SupportsConnectionTest = true,
        SupportsLogin = true,
        SupportsPreCheck = true,
        SupportsResultUpload = true,
        SupportsYieldControl = false,
        SupportsOfflineCache = true
    };

    public IReadOnlyList<MesPluginConfigField> GetConfigFields()
    {
        return new[]
        {
            new MesPluginConfigField { Key = "BaseUrl", DisplayName = "SwiftMOM API 地址", DefaultValue = "http://127.0.0.1:8080/swiftmom/api/", IsRequired = true },
            new MesPluginConfigField { Key = "TimeoutSeconds", DisplayName = "超时秒数", DefaultValue = "5", IsRequired = true }
        };
    }

    public async Task<MesResult> TestConnectionAsync(MesPluginOptions options, CancellationToken cancellationToken = default)
    {
        var baseUrl = GetBaseUrl(options);
        using var client = CreateClient(options);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, "clientLogin"))
            {
                Content = JsonContent.Create(new { userNo = "", password = "" })
            };
            using var response = await client.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode
                ? MesResult.Success($"SwiftMOM 服务可访问：{baseUrl}")
                : MesResult.Fail($"SwiftMOM 服务返回 HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"SwiftMOM 连接失败：{ex.Message}");
        }
    }

    public async Task<MesResult> LoginAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options);
        var response = await PostAsync(client, GetBaseUrl(options), "clientLogin", new
        {
            userNo = request.UserNo,
            password = request.Password
        }, token: string.Empty, cancellationToken);

        if (!response.IsSuccess)
        {
            return response;
        }

        var token = ReadString(response.RawResponse, "token");
        return string.IsNullOrWhiteSpace(token)
            ? MesResult.Fail("clientLogin 成功但未返回 token", response.Code, response.RawResponse)
            : MesResult.Success(response.Message, response.Code, token, response.RawResponse);
    }

    public async Task<MesResult> CheckBeforeTestAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        var login = await LoginAsync(options, request, cancellationToken);
        if (!login.IsSuccess)
        {
            return login;
        }

        using var client = CreateClient(options);
        var response = await PostAsync(client, GetBaseUrl(options), "checkRoute", new
        {
            pcbSeq = request.Barcode,
            prodNo = request.ProdNo,
            stationNo = request.StationNo,
            retest = request.Retest
        }, login.Token, cancellationToken);

        return response.IsSuccess
            ? MesResult.Success(response.Message, response.Code, login.Token, response.RawResponse)
            : response;
    }

    public async Task<MesResult> UploadResultAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(options);
        var token = options.Get("Token");
        if (string.IsNullOrWhiteSpace(token))
        {
            var login = await LoginAsync(options, new MesCheckRequest
            {
                Barcode = request.Barcode,
                ProdNo = request.ProdNo,
                StationNo = request.StationNo,
                Retest = request.Retest,
                UserNo = request.UserNo,
                Password = options.Get("Password")
            }, cancellationToken);

            if (!login.IsSuccess)
            {
                return login;
            }

            token = login.Token;
        }

        var testItemJson = JsonSerializer.Serialize(request.TestItems.Select(item => new
        {
            testItem = item.Name,
            value = item.Value,
            unit = item.Unit,
            result = item.Result,
            upper = item.UpperLimit,
            lower = item.LowerLimit
        }), JsonOptions);

        return await PostAsync(client, GetBaseUrl(options), "createRoute", new
        {
            pcbSeq = request.Barcode,
            prodNo = request.ProdNo,
            stationNo = request.StationNo,
            result = request.Result,
            remark = request.Remark,
            testItem = testItemJson,
            userNo = request.UserNo,
            weight = request.Weight,
            packNo = request.PackNo,
            rmk1 = string.Empty,
            rmk2 = string.Empty,
            rmk3 = string.Empty,
            rmk4 = string.Empty
        }, token, cancellationToken);
    }

    private static async Task<MesResult> PostAsync(HttpClient client, Uri baseUrl, string relativeUrl, object payload, string token, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUrl, relativeUrl))
            {
                Content = JsonContent.Create(payload)
            };

            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Add("token", token);
            }

            using var response = await client.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return MesResult.Fail($"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}", ((int)response.StatusCode).ToString(), raw);
            }

            var msgId = ReadString(raw, "msgId");
            var msgStr = ReadString(raw, "msgStr");
            return msgId == "0"
                ? MesResult.Success(string.IsNullOrWhiteSpace(msgStr) ? "MES 返回成功" : msgStr, msgId, ReadString(raw, "token"), raw)
                : MesResult.Fail(string.IsNullOrWhiteSpace(msgStr) ? "MES 返回失败" : msgStr, string.IsNullOrWhiteSpace(msgId) ? "1" : msgId, raw);
        }
        catch (Exception ex)
        {
            return MesResult.Fail($"MES 请求异常：{ex.Message}");
        }
    }

    private static HttpClient CreateClient(MesPluginOptions options)
    {
        var timeoutSeconds = int.TryParse(options.Get("TimeoutSeconds", "5"), out var seconds) ? seconds : 5;
        return new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds))
        };
    }

    private static Uri GetBaseUrl(MesPluginOptions options)
    {
        var value = options.Get("BaseUrl", "http://127.0.0.1:8080/swiftmom/api/");
        if (!value.EndsWith("/", StringComparison.Ordinal))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }

    private static string ReadString(string json, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty(propertyName, out var value)
                ? value.ToString()
                : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
}

