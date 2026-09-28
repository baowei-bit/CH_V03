using CH_V03.Core.Configuration;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using CH_V03.Core.Mes;
using CH_V03.Core.TestFlow;

namespace CH_V03.App.Services;

public sealed class MesRuntimeService
{
    private static readonly ConcurrentDictionary<string, RecentMesUpload> RecentUploads = new(StringComparer.Ordinal);
    private static readonly TimeSpan UploadDeduplicationWindow = TimeSpan.FromSeconds(30);
    private readonly IReadOnlyList<IMesPlugin> _plugins;

    public MesRuntimeService(string pluginDirectory)
    {
        PluginDirectory = pluginDirectory;
        _plugins = new MesPluginLoader().LoadFromDirectory(pluginDirectory);
    }

    public string PluginDirectory { get; }

    public IReadOnlyList<IMesPlugin> Plugins => _plugins;

    public IMesPlugin? GetSelectedPlugin(AppConfiguration configuration)
    {
        if (!configuration.Mes.Enabled || string.IsNullOrWhiteSpace(configuration.Mes.SelectedPlugin))
        {
            return null;
        }

        return _plugins.FirstOrDefault(plugin =>
            string.Equals(plugin.Id, configuration.Mes.SelectedPlugin, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<MesResult> TestConnectionAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
    {
        var mesAddress = GetMesAddress(configuration);
        if (!Uri.TryCreate(mesAddress, UriKind.Absolute, out var mesUri))
        {
            return MesResult.Fail($"MES 地址无效，无法 Ping：{mesAddress}");
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(mesUri.Host, 2000);
            return reply.Status == IPStatus.Success
                ? MesResult.Success($"MES 主机可达：{mesUri.Host}，延迟 {reply.RoundtripTime} ms")
                : MesResult.Fail($"MES 主机不可达：{mesUri.Host}（{reply.Status}）");
        }
        catch (PingException ex)
        {
            return MesResult.Fail($"MES 主机不可达：{mesUri.Host}（{ex.Message}）");
        }
    }
    public async Task<MesResult> CheckBeforeTestAsync(AppConfiguration configuration, string barcode, CancellationToken cancellationToken = default)
    {
        var plugin = GetSelectedPlugin(configuration);
        if (plugin is null)
        {
            return configuration.Mes.Enabled
                ? MesResult.Fail($"未找到 MES 插件：{configuration.Mes.SelectedPlugin}")
                : MesResult.Success("MES 未启用，跳过卡关");
        }

        if (!plugin.Capabilities.SupportsPreCheck)
        {
            return MesResult.Success($"插件 {plugin.Name} 不支持卡关，已跳过");
        }

        return await plugin.CheckBeforeTestAsync(BuildOptions(configuration), new MesCheckRequest
        {
            Barcode = barcode,
            ProdNo = configuration.Mes.ProdNo,
            StationNo = GetStationNo(configuration),
            UserNo = configuration.Mes.UserNo,
            Password = configuration.Mes.Password,
            Retest = GetMesOption(configuration, "Retest", string.Empty)
        }, cancellationToken);
    }

    public async Task<MesResult> UploadResultAsync(
        AppConfiguration configuration,
        TestFlowSnapshot snapshot,
        IReadOnlyList<MesUploadFile>? files = null,
        CancellationToken cancellationToken = default)
    {
        var plugin = GetSelectedPlugin(configuration);
        if (plugin is null)
        {
            return configuration.Mes.Enabled
                ? MesResult.Fail($"未找到 MES 插件：{configuration.Mes.SelectedPlugin}")
                : MesResult.Success("MES 未启用，跳过上传");
        }

        if (!plugin.Capabilities.SupportsResultUpload)
        {
            return MesResult.Success($"插件 {plugin.Name} 不支持结果上传，已跳过");
        }

        var options = BuildOptions(configuration);
        var request = new MesUploadRequest
        {
            Barcode = snapshot.Barcode,
            ProdNo = configuration.Mes.ProdNo,
            StationNo = GetStationNo(configuration),
            Result = snapshot.IsPass == true ? "PASS" : "FAIL",
            // 昆山嘉联益 CareerRule 接口不应接收软件内部中文状态文案（例如“测试结束：FAIL”）。
            // 详细不良信息已包含在 TestData 中，故专用模式下保持 Defect 为空。
            Remark = configuration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration
                ? string.Empty
                : snapshot.Message,
            UserNo = configuration.Mes.UserNo,
            PackNo = configuration.Mes.PackNo,
            Retest = GetMesOption(configuration, "Retest", string.Empty),
            Weight = GetMesOption(configuration, "Weight", string.Empty),
            StartTime = snapshot.StartTime ?? DateTime.Now,
            EndTime = snapshot.EndTime ?? DateTime.Now,
            TestItems = snapshot.Items.Select(item => new MesTestItem
            {
                Name = item.ItemName,
                Value = item.TestValue,
                Unit = item.Unit,
                Result = item.Result,
                UpperLimit = item.UpperLimit,
                LowerLimit = item.LowerLimit
            }).ToList(),
            Files = files ?? Array.Empty<MesUploadFile>()
        };

        CleanupExpiredUploads();
        var deduplicationKey = BuildUploadDeduplicationKey(plugin.Id, request);
        var now = DateTimeOffset.UtcNow;

        while (true)
        {
            if (RecentUploads.TryGetValue(deduplicationKey, out var existing))
            {
                if (now - existing.CreatedAt <= UploadDeduplicationWindow)
                {
                    return MesResult.Success("检测到短时间重复 MES 上传，已跳过重复请求", "DUPLICATE");
                }

                RecentUploads.TryRemove(deduplicationKey, out _);
                continue;
            }

            var current = new RecentMesUpload(now);
            if (RecentUploads.TryAdd(deduplicationKey, current))
            {
                var result = await plugin.UploadResultAsync(options, request, cancellationToken);
                if (!result.IsSuccess)
                {
                    RecentUploads.TryRemove(deduplicationKey, out _);
                }

                return result;
            }
        }
    }

    private static MesPluginOptions BuildOptions(AppConfiguration configuration)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BaseUrl"] = configuration.Mes.BaseUrl,
            ["UserNo"] = configuration.Mes.UserNo,
            ["Password"] = configuration.Mes.Password,
            ["TimeoutSeconds"] = "5",
            ["Station"] = GetStationNo(configuration),
            ["ModifyUser"] = FirstValue(configuration.Mes.UserNo, configuration.User.LoginJobNo),
            ["Tooling"] = FirstValue(configuration.Mes.Tooling, configuration.User.FixtureNo, configuration.Mes.PackNo),
            ["FixtureNo"] = FirstValue(configuration.User.FixtureNo, configuration.Mes.Tooling),
            ["JobNo"] = configuration.Mes.ProdNo,
            ["PackNo"] = configuration.Mes.PackNo,
            ["SwVersion"] = configuration.Identity.TestProgram,
            ["ProductName"] = configuration.Identity.ProductName,
            ["Program"] = configuration.Identity.ProductName,
            ["TestType"] = configuration.Identity.TestType,
            ["IpAddress"] = configuration.Identity.MachineNo,
            // 不扫码模式由奕东 EAP 插件识别：只上报治具累计信息，不能将软件生成的
            // NOSCAN 虚拟条码发送到 SN 过站接口。
            ["NoBarcodeMode"] = configuration.User.NoScanMode ? "Y" : "N"
        };

        foreach (var item in configuration.Mes.PluginOptions)
        {
            values[item.Key] = item.Value;
        }

        if (string.Equals(configuration.Mes.SelectedPlugin, "HongXin.OA", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(configuration.Mes.HongXinProductionFlowNo))
            {
                values["MesStartScRcno"] = configuration.Mes.HongXinProductionFlowNo;
                values["MesCapacityScRcno"] = configuration.Mes.HongXinProductionFlowNo;
                values["MesStationScRcno"] = configuration.Mes.HongXinProductionFlowNo;
            }
        }

        return new MesPluginOptions
        {
            Values = values
        };
    }

    private static string GetStationNo(AppConfiguration configuration)
    {
        return string.IsNullOrWhiteSpace(configuration.Identity.StationNo)
            ? configuration.Mes.StationNo
            : configuration.Identity.StationNo;
    }

    private static string GetMesAddress(AppConfiguration configuration)
    {
        return configuration.Mes.PluginOptions.TryGetValue("BaseUrl", out var pluginBaseUrl) &&
               !string.IsNullOrWhiteSpace(pluginBaseUrl)
            ? pluginBaseUrl
            : configuration.Mes.BaseUrl;
    }

    private static string FirstValue(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static string GetMesOption(AppConfiguration configuration, string key, string defaultValue)
    {
        return configuration.Mes.PluginOptions.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    private static string BuildUploadDeduplicationKey(string pluginId, MesUploadRequest request)
    {
        var itemSignature = string.Join('|', request.TestItems.Select(item => string.Join('\u001f',
            Normalize(item.Name),
            Normalize(item.Value),
            Normalize(item.Result),
            Normalize(item.UpperLimit),
            Normalize(item.LowerLimit),
            Normalize(item.Unit))));

        return string.Join('\u001e',
            Normalize(pluginId),
            Normalize(request.Barcode),
            Normalize(request.ProdNo),
            Normalize(request.StationNo),
            Normalize(request.Result),
            Normalize(request.PackNo),
            itemSignature);
    }

    private static string Normalize(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToUpperInvariant();
    }

    private static void CleanupExpiredUploads()
    {
        var cutoff = DateTimeOffset.UtcNow - UploadDeduplicationWindow;
        foreach (var item in RecentUploads)
        {
            if (item.Value.CreatedAt < cutoff)
            {
                RecentUploads.TryRemove(item.Key, out _);
            }
        }
    }

    private sealed record RecentMesUpload(DateTimeOffset CreatedAt);
}


