using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Windows.Input;
using CH_V01.App.Commands;
using CH_V01.Core.Configuration;

namespace CH_V01.App.ViewModels;

public sealed class MesDebugToolViewModel : DialogViewModelBase
{
    private string _method = "GET";
    private string _url = string.Empty;
    private string _query = string.Empty;
    private string _body = string.Empty;
    private string _headers = string.Empty;
    private string _contentType = "application/json";
    private string _response = string.Empty;
    private string _status = "未发送";
    private bool _isBusy;

    public MesDebugToolViewModel(AppConfiguration configuration)
    {
        Methods = new[] { "GET", "POST" };
        Url = ResolveDefaultUrl(configuration);
        Query = BuildDefaultPreCheckQuery(configuration);
        Body = BuildDefaultUploadJson(configuration);
        SendCommand = new AsyncRelayCommand(SendAsync);
        ClearCommand = new RelayCommand(Clear);
        CloseCommand = new RelayCommand(() => Close(false));
    }

    public IReadOnlyList<string> Methods { get; }

    public string Method
    {
        get => _method;
        set
        {
            var method = string.IsNullOrWhiteSpace(value) ? "GET" : value.ToUpperInvariant();
            if (SetField(ref _method, method))
            {
                OnPropertyChanged(nameof(IsGetSelected));
                OnPropertyChanged(nameof(IsPostSelected));
            }
        }
    }

    public bool IsGetSelected => string.Equals(Method, "GET", StringComparison.OrdinalIgnoreCase);

    public bool IsPostSelected => string.Equals(Method, "POST", StringComparison.OrdinalIgnoreCase);

    public string Url
    {
        get => _url;
        set => SetField(ref _url, value ?? string.Empty);
    }

    public string Query
    {
        get => _query;
        set => SetField(ref _query, value ?? string.Empty);
    }

    public string Body
    {
        get => _body;
        set => SetField(ref _body, value ?? string.Empty);
    }

    /// <summary>
    /// 一行一个 HTTP 请求头，格式为：名称: 值。
    /// 例如 Authorization: Bearer &lt;token&gt; 或 Token: abc123。
    /// </summary>
    public string Headers
    {
        get => _headers;
        set => SetField(ref _headers, value ?? string.Empty);
    }

    public string ContentType
    {
        get => _contentType;
        set => SetField(ref _contentType, string.IsNullOrWhiteSpace(value) ? "application/json" : value);
    }

    public string Response
    {
        get => _response;
        private set => SetField(ref _response, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public ICommand SendCommand { get; }

    public ICommand ClearCommand { get; }

    public ICommand CloseCommand { get; }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            Status = "URL 不能为空";
            return;
        }

        IsBusy = true;
        Status = "请求中...";
        Response = string.Empty;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var isGet = string.Equals(Method, "GET", StringComparison.OrdinalIgnoreCase);
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(new HttpMethod(Method), BuildUri(isGet));
            if (!isGet)
            {
                request.Content = new StringContent(Body ?? string.Empty, Encoding.UTF8, ContentType);
            }

            ApplyHeaders(request, Headers);

            using var response = await client.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();
            Status = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}，耗时 {stopwatch.ElapsedMilliseconds} ms";
            Response = responseText;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Status = $"请求失败，耗时 {stopwatch.ElapsedMilliseconds} ms";
            Response = ex.ToString();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Uri BuildUri(bool includeQuery)
    {
        var url = Url.Trim();
        if (!includeQuery)
        {
            return new Uri(url);
        }

        var query = (Query ?? string.Empty).Trim().TrimStart('?');
        if (string.IsNullOrWhiteSpace(query))
        {
            return new Uri(url);
        }

        var separator = url.Contains('?') ? "&" : "?";
        return new Uri(url + separator + query);
    }

    private static void ApplyHeaders(HttpRequestMessage request, string headerText)
    {
        foreach (var rawLine in (headerText ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                throw new InvalidOperationException($"请求头格式无效：{line}。请使用“名称: 值”格式。");
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            var added = name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase) && request.Content is not null
                ? request.Content.Headers.TryAddWithoutValidation(name, value)
                : request.Headers.TryAddWithoutValidation(name, value);
            if (!added)
            {
                throw new InvalidOperationException($"无法添加请求头：{name}");
            }
        }
    }

    private void Clear()
    {
        Response = string.Empty;
        Status = "已清空";
    }

    private static string ResolveDefaultUrl(AppConfiguration configuration)
    {
        if (configuration.Mes.PluginOptions.TryGetValue("PreCheckUrl", out var preCheckUrl) && !string.IsNullOrWhiteSpace(preCheckUrl))
        {
            return preCheckUrl;
        }

        if (configuration.Mes.PluginOptions.TryGetValue("UploadUrl", out var uploadUrl) && !string.IsNullOrWhiteSpace(uploadUrl))
        {
            return uploadUrl;
        }

        return configuration.Mes.BaseUrl;
    }

    private static string BuildDefaultPreCheckQuery(AppConfiguration configuration)
    {
        var testBarcode = GetPluginOption(configuration, "ConnectionTestBarcode", "TEST");
        var values = new Dictionary<string, string>
        {
            ["barcode"] = testBarcode,
            ["operator"] = FirstValue(configuration.Mes.UserNo, configuration.User.LoginJobNo),
            ["toolNumber"] = FirstValue(configuration.Mes.Tooling, configuration.User.FixtureNo, configuration.Mes.PackNo),
            ["TestType"] = configuration.Identity.TestType,
            ["PrevTestType"] = GetPluginOption(configuration, "PrevTestType", "NULL"),
            ["ProductName"] = configuration.Identity.ProductName,
            ["SlotName"] = GetPluginOption(configuration, "SlotName", "1"),
            ["subSlotName"] = GetPluginOption(configuration, "SubSlotName", "A"),
            ["workArea"] = GetPluginOption(configuration, "WorkArea", string.Empty),
            ["site"] = GetPluginOption(configuration, "Site", string.Empty),
            ["program"] = FirstValue(GetPluginOption(configuration, "Program", string.Empty), configuration.Identity.ProductName),
            ["lot"] = GetPluginOption(configuration, "Lot", "NULL"),
            ["ipAddress"] = configuration.Identity.MachineNo,
            ["stationTime"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
        };

        return string.Join("&", values
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));
    }

    private static string BuildDefaultUploadJson(AppConfiguration configuration)
    {
        var testBarcode = GetPluginOption(configuration, "ConnectionTestBarcode", "TEST");
        var userNo = FirstValue(configuration.Mes.UserNo, configuration.User.LoginJobNo);
        var fixture = FirstValue(configuration.Mes.Tooling, configuration.User.FixtureNo, configuration.Mes.PackNo);
        var product = configuration.Identity.ProductName;
        return $$"""
        {
          "barcode": "{{testBarcode}}",
          "testType": "{{configuration.Identity.TestType}}",
          "testTime": "{{DateTime.Now:yyyy-MM-ddTHH:mm:ss}}",
          "testResult": "PASS",
          "operator": "{{userNo}}",
          "workArea": "{{GetPluginOption(configuration, "WorkArea", string.Empty)}}",
          "resourceName": "{{fixture}}",
          "ipAddress": "{{configuration.Identity.MachineNo}}",
          "toolNumber": "{{fixture}}",
          "slotName": "{{GetPluginOption(configuration, "SlotName", "1")}}",
          "subslotName": "{{GetPluginOption(configuration, "SubSlotName", "A")}}",
          "errorCode": "",
          "errorValue": "",
          "productName": "{{product}}",
          "program": "{{FirstValue(GetPluginOption(configuration, "Program", string.Empty), product)}}",
          "testDetailId": "{{Guid.NewGuid()}}",
          "testDetails": []
        }
        """;
    }

    private static string GetPluginOption(AppConfiguration configuration, string key, string defaultValue)
    {
        return configuration.Mes.PluginOptions.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    private static string FirstValue(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
