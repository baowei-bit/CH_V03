using BW_V01.Core.Configuration;
using BW_V01.Core.Mes;
using BW_V01.Core.TestFlow;

namespace BW_V01.App.Services;

public sealed class MesRuntimeService
{
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
        var plugin = GetSelectedPlugin(configuration);
        if (plugin is null)
        {
            return configuration.Mes.Enabled
                ? MesResult.Fail($"未找到 MES 插件：{configuration.Mes.SelectedPlugin}")
                : MesResult.Fail("MES 未启用，不能切换在线模式");
        }

        return await plugin.TestConnectionAsync(BuildOptions(configuration), cancellationToken);
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
            Retest = string.Empty
        }, cancellationToken);
    }

    public async Task<MesResult> UploadResultAsync(AppConfiguration configuration, TestFlowSnapshot snapshot, CancellationToken cancellationToken = default)
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

        return await plugin.UploadResultAsync(BuildOptions(configuration), new MesUploadRequest
        {
            Barcode = snapshot.Barcode,
            ProdNo = configuration.Mes.ProdNo,
            StationNo = GetStationNo(configuration),
            Result = snapshot.IsPass == true ? "PASS" : "FAIL",
            Remark = snapshot.Message,
            UserNo = configuration.Mes.UserNo,
            PackNo = configuration.Mes.PackNo,
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
            }).ToList()
        }, cancellationToken);
    }

    private static MesPluginOptions BuildOptions(AppConfiguration configuration)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["BaseUrl"] = configuration.Mes.BaseUrl,
            ["Password"] = configuration.Mes.Password,
            ["TimeoutSeconds"] = "5",
            ["Station"] = GetStationNo(configuration),
            ["ModifyUser"] = FirstValue(configuration.Mes.UserNo, configuration.User.LoginJobNo),
            ["Tooling"] = FirstValue(configuration.Mes.Tooling, configuration.User.FixtureNo, configuration.Mes.PackNo),
            ["JobNo"] = configuration.Mes.ProdNo,
            ["PackNo"] = configuration.Mes.PackNo,
            ["SwVersion"] = configuration.Identity.TestProgram,
            ["ProductName"] = configuration.Identity.ProductName,
            ["Program"] = configuration.Identity.ProductName,
            ["TestType"] = configuration.Identity.TestType,
            ["IpAddress"] = configuration.Identity.MachineNo
        };

        foreach (var item in configuration.Mes.PluginOptions)
        {
            values[item.Key] = item.Value;
        }

        return new MesPluginOptions
        {
            Values = values
        };
    }

    private static string GetStationNo(AppConfiguration configuration)
    {
        return string.IsNullOrWhiteSpace(configuration.Mes.StationNo)
            ? configuration.Identity.StationNo
            : configuration.Mes.StationNo;
    }

    private static string FirstValue(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}


