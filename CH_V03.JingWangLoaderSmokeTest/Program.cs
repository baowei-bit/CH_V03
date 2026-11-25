using System.Reflection;
using System.Data.Common;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using CH_V03.App.Services;
using CH_V03.Core.Configuration;
using CH_V03.Core.Mes;

[assembly: SupportedOSPlatform("windows")]

// 此宿主仅引用 Core，刻意不引用 Web SDK、景旺插件或 MySqlConnector，模拟上位机的动态加载环境。
if (args.Length is < 1 or > 2)
    throw new ArgumentException("请传入上位机输出目录中的 plugins/mes 路径");
var expectMissingComponent = args.Length == 2 && args[1] == "--expect-missing-component";

var plugin = new MesPluginLoader().LoadFromDirectory(Path.GetFullPath(args[0]))
    .SingleOrDefault(item => item.Id == "JingWang.Eap")
    ?? throw new InvalidOperationException("未能动态加载景旺插件");

try
{
    if (!expectMissingComponent)
    {
        var context = AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly)!;
        var connector = context.LoadFromAssemblyName(new AssemblyName("MySqlConnector"));
        using var connection = (IDisposable)Activator.CreateInstance(connector.GetType("MySqlConnector.MySqlConnection", throwOnError: true)!)!;
    }
}
catch (Exception ex)
{
    // 尚未提供连接字符串，此处异常只涉及组件初始化，可以输出根因以验证部署依赖。
    Console.Error.WriteLine($"动态加载初始化失败：{ex.GetBaseException().GetType().Name}：{ex.GetBaseException().Message}");
    return 1;
}

var password = Environment.GetEnvironmentVariable("JINGWANG_TEST_DB_PASSWORD");
if (string.IsNullOrWhiteSpace(password) && !expectMissingComponent)
    throw new InvalidOperationException("需要设置 JINGWANG_TEST_DB_PASSWORD 环境变量");
if (args.Length == 2 && args[1] == "--inspect-recent")
{
    var context = AssemblyLoadContext.GetLoadContext(plugin.GetType().Assembly)!;
    var connector = context.LoadFromAssemblyName(new AssemblyName("MySqlConnector"));
    await using var connection = (DbConnection)Activator.CreateInstance(connector.GetType("MySqlConnector.MySqlConnection", true)!)!;
    connection.ConnectionString = new DbConnectionStringBuilder
    {
        ["Server"] = "127.0.0.1", ["Port"] = 3306, ["Database"] = "pc_eap", ["User ID"] = "root",
        ["Password"] = password, ["SSL Mode"] = "None"
    }.ConnectionString;
    await connection.OpenAsync();
    foreach (var table in new[] { "pc_insp_data", "pc_insph_data", "pc_img_data", "pc_img_rel_data", "pc_data_scan", "pc_data_craft", "pc_data_alarm", "pc_data_emp" })
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*), MAX(CREATETIME) FROM `{table}`";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        Console.WriteLine($"{table}: count={reader.GetInt64(0)}, latest={(reader.IsDBNull(1) ? "无" : reader.GetDateTime(1).ToString("yyyy-MM-dd HH:mm:ss"))}");
    }
    await using (var relationCheck = connection.CreateCommand())
    {
        relationCheck.CommandText = "SELECT h.PCSID,h.PASSYN,h.DATAGUID,i.FILETYPE,i.IMGCONTENT,(SELECT COUNT(*) FROM pc_insp_data d WHERE d.DATAGUID=h.DATAGUID) FROM pc_insph_data h JOIN pc_img_rel_data r ON r.DATAGUID=h.DATAGUID JOIN pc_img_data i ON i.IMGGUID=r.IMGGUID ORDER BY h.ID DESC LIMIT 3";
        await using var reader = await relationCheck.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            Console.WriteLine($"正式库关联核对：条码={reader.GetString(0)}，PASSYN={reader.GetString(1)}，DATAGUID={reader.GetString(2)}，明细={reader.GetInt64(5)}，文件类型={reader.GetString(3)}，IMGCONTENT={reader.GetString(4)}");
    }
    await using var cleanupCheck = connection.CreateCommand();
    cleanupCheck.CommandText = "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE LEFT(SCHEMA_NAME,9)='jw_smoke_'";
    Console.WriteLine($"临时测试库剩余：{await cleanupCheck.ExecuteScalarAsync()}");
    return 0;
}
var result = await plugin.TestConnectionAsync(new MesPluginOptions
{
    Values = new Dictionary<string, string>
    {
        ["DbHost"] = "127.0.0.1", ["DbPort"] = "3306", ["DbName"] = "pc_eap", ["DbUser"] = "root", ["DbPassword"] = password ?? ""
    }
});
Console.WriteLine(result.Message);
if (expectMissingComponent)
    return !result.IsSuccess && result.Message.Contains("Microsoft.Extensions.Logging.Abstractions", StringComparison.Ordinal)
        && !result.Message.Contains("TypeInitializationException", StringComparison.Ordinal) ? 0 : 1;
if (!result.IsSuccess) return 1;
foreach (var webEnabled in new[] { "true", "false" })
{
    var disabledEmployeeOptions = new MesPluginOptions
    {
        Values = new Dictionary<string, string> { ["EnableEmployee"] = "false", ["EnableWebApi"] = webEnabled }
    };
    if (!(await plugin.LoginAsync(disabledEmployeeOptions, new MesCheckRequest())).IsSuccess
        || !(await ((IJingWangMesPlugin)plugin).EndShiftAsync(disabledEmployeeOptions)).IsSuccess)
        throw new InvalidOperationException("交付插件在员工功能关闭时仍阻止连接或离线");
}
Console.WriteLine("交付插件已验证：员工功能关闭时跳过上机及下机流程。");
// 复用主界面实际调用的运行服务，覆盖配置序列化后到连接、上机和过站的整条路径。
var runtime = new MesRuntimeService(Path.GetFullPath(args[0]));
foreach (var employeeEnabled in new[] { "true", "false" })
{
    var config = new AppConfiguration();
    config.Mes.Enabled = true;
    config.Mes.SelectedPlugin = "JingWang.Eap";
    config.Mes.PluginOptions = new Dictionary<string, string>
    {
        ["DbHost"] = "127.0.0.1", ["DbPort"] = "3306", ["DbName"] = "pc_eap", ["DbUser"] = "root",
        ["DbPasswordProtected"] = MesSecretProtector.Protect(password!),
        ["EnableWebApi"] = "false", ["EnableEmployee"] = employeeEnabled
    };
    var reloaded = ConfigurationStore.Clone(config);
    if (!(await runtime.TestConnectionAsync(reloaded)).IsSuccess
        || !(await runtime.StartJingWangShiftAsync(reloaded)).IsSuccess)
        throw new InvalidOperationException("Web API 关闭时，主界面连接流程仍被员工上机阻止");
    var check = await runtime.CheckBeforeTestAsync(reloaded, "JW_SWITCH_TEST");
    if (!check.IsSuccess || check.Code != "SKIPPED")
        throw new InvalidOperationException("Web API 关闭时未跳过过站接口，或错误地标记为已过站");
}
Console.WriteLine("主界面调用链已验证：配置重载后关闭 Web API，仅检查数据库，跳过上机和过站。");
return 0;
