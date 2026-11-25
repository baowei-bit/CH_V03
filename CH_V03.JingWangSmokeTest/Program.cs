using CH_V03.Core.Mes;
using CH_V03.Core.Configuration;
using CH_V03.Core.ResultExport;
using CH_V03.Core.TestFlow;
using CH_V03.Mes.JingWang;
using MySqlConnector;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Text.Json;

var checks = new List<string>();
var tableCounts = new Dictionary<string, int>();
var columnTypes = new Dictionary<string, Dictionary<string, string>>();
var sampleRows = new Dictionary<string, Dictionary<string, string>>();
var startedAt = DateTimeOffset.Now;
var reportIndex = Array.IndexOf(args, "--report");
var reportPath = reportIndex >= 0 && reportIndex + 1 < args.Length ? Path.GetFullPath(args[reportIndex + 1]) : null;
var password = Environment.GetEnvironmentVariable("JINGWANG_TEST_DB_PASSWORD");
if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("需要设置 JINGWANG_TEST_DB_PASSWORD 环境变量");
var schema = "jw_smoke_" + Guid.NewGuid().ToString("N")[..12];
var journal = Path.Combine(Path.GetTempPath(), "CeHao-JingWang-Smoke-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(journal);
var uploaded = new ConcurrentDictionary<string, byte[]>();
var dbPort = uint.Parse(Environment.GetEnvironmentVariable("JINGWANG_TEST_DB_PORT") ?? "3306");
var admin = new MySqlConnectionStringBuilder { Server = "127.0.0.1", Port = dbPort, UserID = "root", Password = password, SslMode = MySqlSslMode.None };
var tables = new[] { "pc_data_craft", "pc_data_scan", "pc_data_alarm", "pc_data_emp", "pc_insp_data", "pc_insph_data", "pc_img_data", "pc_img_rel_data" };

await using var connection = new MySqlConnection(admin.ConnectionString);
await connection.OpenAsync();
try
{
    await ExecuteAsync($"CREATE DATABASE `{schema}` CHARACTER SET utf8mb4");
    foreach (var table in tables) await ExecuteAsync($"CREATE TABLE `{schema}`.`{table}` LIKE `pc_eap`.`{table}`");

    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    var builder = WebApplication.CreateBuilder(Array.Empty<string>());
    builder.Logging.ClearProviders();
    builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
    var app = builder.Build();
    var loginCalls = 0;
    app.MapPost("/login", (System.Text.Json.JsonElement body) =>
    {
        Interlocked.Increment(ref loginCalls);
        return body.GetProperty("CardCode").GetString() == "DENY"
            ? Results.Json(new { EmpName = "", EmpNo = "", Permit = "0", Code = "0" })
            : Results.Json(new { EmpName = "测试员工", EmpNo = "JW_TEST", Permit = "1", Code = "0" });
    });
    var stationCalls = 0;
    app.MapPost("/station", (JsonElement body) =>
    {
        Interlocked.Increment(ref stationCalls);
        var sn = body.GetProperty("sn").GetString();
        var allowed = sn != "JW_REJECT" && (sn != "JW_RETRY_API" || body.GetProperty("woCode").GetString() == "JW_WO");
        return Results.Json(new { Result = allowed ? 1 : 0, Message = "过站结果" });
    });
    var recheckCalls = 0;
    var attempts = new ConcurrentDictionary<string, int>();
    app.MapPost("/recheck", async (JsonElement body, HttpContext context) =>
    {
        Interlocked.Increment(ref recheckCalls);
        var sn = body[0].GetProperty("sn").GetString() ?? "";
        var attempt = attempts.AddOrUpdate(sn, 1, (_, count) => count + 1);
        if (sn == "JW_TIMEOUT") await Task.Delay(2000, context.RequestAborted);
        var status = sn == "JW_RETRY_API" && (attempt <= 2 || body[0].GetProperty("woCode").GetString() != "JW_WO") ? 0 : 1;
        return Results.Json(new
        {
            status, Message = "模拟复判", DataList = new[] { new { result = sn == "JW_RETEST" ? "RETEST" : "OK", pcsCode = sn == "JW_MISMATCH" ? "OTHER_SN" : sn } }
        });
    });
    app.MapPut("/test-bucket/{**key}", async (string key, HttpRequest request) =>
    {
        using var bytes = new MemoryStream();
        await request.Body.CopyToAsync(bytes);
        uploaded[key] = bytes.ToArray();
        return Results.Ok();
    });
    await app.StartAsync();
    try
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DbHost"] = "127.0.0.1", ["DbPort"] = dbPort.ToString(), ["DbName"] = schema, ["DbUser"] = "root", ["DbPassword"] = password,
            ["JournalDirectory"] = journal, ["EquipmentCode"] = "JW_EQ", ["EmployeeCardCode"] = "JW_CARD",
            ["WorkOrder"] = "JW_WO", ["SpecCode"] = "JW_SPEC", ["Product"] = "JW_PRODUCT", ["Container"] = "JW_LOT",
            ["SetId"] = "JW_SET", ["PanelSequence"] = "1", ["CraftMappings"] = "R1=R_CODE|电阻|Ω",
            ["S3Endpoint"] = $"http://127.0.0.1:{port}", ["S3Bucket"] = "test-bucket", ["S3Prefix"] = "ICT/",
            ["S3AccessKey"] = "test", ["S3SecretKey"] = "local-secret",
            ["LoginUrl"] = $"http://127.0.0.1:{port}/login", ["StationUrl"] = $"http://127.0.0.1:{port}/station",
            ["RecheckUrl"] = $"http://127.0.0.1:{port}/recheck", ["OnlineMode"] = "true"
        };
        var options = new MesPluginOptions { Values = values };
        var plugin = new JingWangMesPlugin();
        Check((await plugin.TestConnectionAsync(options)).IsSuccess, "数据库连通与表检查");
        foreach (var webEnabled in new[] { "true", "false" })
        foreach (var loginUrl in new[] { values["LoginUrl"], "" })
        {
            var employeeDisabled = new MesPluginOptions
            {
                Values = new Dictionary<string, string>(values)
                {
                    ["EnableEmployee"] = "false", ["EnableWebApi"] = webEnabled, ["LoginUrl"] = loginUrl,
                    ["EquipmentCode"] = "", ["EmployeeCardCode"] = ""
                }
            };
            var skippedPlugin = new JingWangMesPlugin();
            var callsBefore = Volatile.Read(ref loginCalls);
            Check((await skippedPlugin.LoginAsync(employeeDisabled, new MesCheckRequest())).IsSuccess, "员工功能关闭时允许连接");
            Check((await skippedPlugin.EndShiftAsync(employeeDisabled)).IsSuccess, "员工功能关闭时允许离线");
            Check(skippedPlugin.CurrentEmployeeNo == "" && Volatile.Read(ref loginCalls) == callsBefore, "员工功能关闭时不调用上机接口或创建会话");
            Check(await CountAsync("pc_data_emp") == 0, "员工功能关闭时不写上下机记录");
        }
        var missingLoginUrl = new MesPluginOptions { Values = new Dictionary<string, string>(values) { ["LoginUrl"] = "" } };
        var missingLoginResult = await new JingWangMesPlugin().LoginAsync(missingLoginUrl, new MesCheckRequest());
        Check(!missingLoginResult.IsSuccess && missingLoginResult.Message.Contains("员工上机 API 地址未配置或无效"), "员工功能启用时清楚提示缺少接口地址");
        var databaseOnly = new MesPluginOptions
        {
            Values = new Dictionary<string, string>(values)
            {
                ["EnableEmployee"] = "true", ["EnableWebApi"] = "false", ["LoginUrl"] = "",
                ["StationUrl"] = "", ["RecheckUrl"] = "", ["EquipmentCode"] = "", ["EmployeeCardCode"] = ""
            }
        };
        var databaseOnlyPlugin = new JingWangMesPlugin();
        var callsBeforeDatabaseOnly = Volatile.Read(ref loginCalls);
        Check((await databaseOnlyPlugin.LoginAsync(databaseOnly, new MesCheckRequest())).IsSuccess, "关闭 Web API 时员工功能开启也不阻止连接");
        Check((await databaseOnlyPlugin.CheckBeforeTestAsync(databaseOnly, new MesCheckRequest { Barcode = "JW_DATABASE_ONLY" })).Code == "SKIPPED", "关闭 Web API 时跳过过站且不视为过站通过");
        Check(Volatile.Read(ref loginCalls) == callsBeforeDatabaseOnly && await CountAsync("pc_data_emp") == 0, "只连数据库时不调用员工接口或写入上机记录");
        var denied = new Dictionary<string, string>(values) { ["EmployeeCardCode"] = "DENY" };
        Check(!(await new JingWangMesPlugin().LoginAsync(new MesPluginOptions { Values = denied }, new MesCheckRequest())).IsSuccess, "员工上机拒绝");
        Check((await plugin.LoginAsync(options, new MesCheckRequest())).IsSuccess, "员工上机");
        Check((await plugin.RecordScanAsync(options, "JW_TEST_SN", "JW_TEST")).IsSuccess, "设备读码");
        Check((await plugin.RecordAlarmAsync(options, "SCANNER_FAILURE", "扫码故障", true)).IsSuccess, "报警发生");
        Check((await plugin.RecordAlarmAsync(options, "SCANNER_FAILURE", "扫码恢复", false)).IsSuccess, "报警复位");
        Check((await plugin.CollectCraftAsync(options, [new MesTestItem { Name = "R1", Value = "5.01", Unit = "Ω", Result = "OK" }], DateTime.Now)).IsSuccess, "工艺质量");
        Check((await plugin.CheckBeforeTestAsync(options, new MesCheckRequest { Barcode = "JW_TEST_SN" })).IsSuccess, "过站校验");
        Check(!(await plugin.CheckBeforeTestAsync(options, new MesCheckRequest { Barcode = "JW_REJECT" })).IsSuccess, "过站拒绝");
        var csv = Path.Combine(journal, "JW_TEST_SN.csv");
        await File.WriteAllTextAsync(csv, "SN,Result\nJW_TEST_SN,OK\n");
        var request = new MesUploadRequest
        {
            Barcode = "JW_TEST_SN", UserNo = plugin.CurrentEmployeeNo, Result = "PASS", PrecheckStatus = "已过站",
            StartTime = DateTime.Now.AddSeconds(-5), EndTime = DateTime.Now,
            TestItems = [new MesTestItem { Name = "R1", Value = "5.01", Unit = "Ω", Result = "OK", LowerLimit = "4", UpperLimit = "6" }],
            Files = [new MesUploadFile { FilePath = csv }]
        };
        Check((await plugin.UploadResultAsync(options, request)).IsSuccess, "检测记录、结果、CSV 和复判");
        Check((await plugin.UploadResultAsync(options, request)).Code == "DUPLICATE", "重复提交去重");
        Check(await CountAsync("pc_insp_data") == 0, "全部通过时不写不良明细");
        Check(await CountAsync("pc_insph_data") == 1, "检测结果数量");
        Check(await CountAsync("pc_img_data") == 1 && await CountAsync("pc_img_rel_data") == 1, "CSV 关联数量");
        Check(await CountAsync("pc_data_scan") == 1 && await CountAsync("pc_data_craft") == 1, "扫码和工艺数量");
        Check(await CountAsync("pc_data_alarm") == 2, "报警发生与复位数量");
        await CheckGuidRelationsAsync(request.Barcode, csv, "1");
        var secondCsv = Path.Combine(journal, "JW_TEST_SN_retest.csv");
        await File.WriteAllTextAsync(secondCsv, "SN,Result\nJW_TEST_SN,NG\n");
        var secondRequest = new MesUploadRequest
        {
            Barcode = request.Barcode, UserNo = request.UserNo, Result = "FAIL", PrecheckStatus = "已过站",
            StartTime = request.StartTime.AddSeconds(10), EndTime = request.EndTime.AddSeconds(10),
            TestItems = [new MesTestItem { Name = "R1", Value = "7.01", Unit = "Ω", Result = "NG", LowerLimit = "4", UpperLimit = "6" }],
            Files = [new MesUploadFile { FilePath = secondCsv }]
        };
        Check((await plugin.UploadResultAsync(options, secondRequest)).IsSuccess, "同一条码再次测试写入独立记录");
        await CheckGuidRelationsAsync(request.Barcode, secondCsv, "0");
        await CheckGuidRelationsAsync(request.Barcode, csv, "1");
        Check((await new JingWangMesPlugin().UploadResultAsync(options, secondRequest)).Code == "DUPLICATE", "重建插件后同一笔测试仍去重");
        Check(await CountAsync("pc_insph_data") == 2 && await CountAsync("pc_img_rel_data") == 2 && await CountAsync("pc_img_data") == 2,
            "同一条码两次测试保留两组 GUID 关联");
        MesUploadRequest AdditionalRequest(string barcode, int seconds) => new()
        {
            Barcode = barcode, UserNo = request.UserNo, Result = request.Result, PrecheckStatus = request.PrecheckStatus,
            StartTime = request.StartTime.AddSeconds(seconds), EndTime = request.EndTime.AddSeconds(seconds),
            TestItems = request.TestItems
        };
        var retestRequest = AdditionalRequest("JW_RETEST", 1);
        var retestOptions = new MesPluginOptions { Values = new Dictionary<string, string>(values) { ["EnableCsv"] = "false", ["EnableCsvRelation"] = "false" } };
        Check((await plugin.UploadResultAsync(retestOptions, retestRequest)).IsSuccess, "复判要求重测");
        Check(plugin.GetPendingRecords(retestOptions).Any(item => item.Barcode == "JW_RETEST" && item.Status.Contains("RETEST")), "重测记录留待人工核对");
        var retestPending = plugin.GetPendingRecords(retestOptions).Single(item => item.Barcode == "JW_RETEST");
        Check(plugin.ArchivePendingRecord(retestOptions, retestPending.Id).IsSuccess, "人工核对后归档");
        Check(!plugin.GetPendingRecords(retestOptions).Any(item => item.Barcode == "JW_RETEST"), "已归档记录退出待处理列表");
        var unavailable = new MesPluginOptions { Values = new Dictionary<string, string>(values) { ["DbPort"] = "1", ["EnableWebApi"] = "false", ["EnableCsv"] = "false", ["EnableCsvRelation"] = "false" } };
        var pendingRequest = AdditionalRequest("JW_PENDING", 2);
        Check(!(await plugin.UploadResultAsync(unavailable, pendingRequest)).IsSuccess, "数据库不可用时保留待写入");
        Check(plugin.GetPendingRecords(unavailable).Any(item => item.Barcode == "JW_PENDING"), "断库记录可见");
        var restartedPlugin = new JingWangMesPlugin();
        Check((await restartedPlugin.RetryPendingDatabaseAsync(retestOptions, plugin.GetPendingRecords(retestOptions).Single(item => item.Barcode == "JW_PENDING").Id)).IsSuccess, "重启后人工补写");
        var databaseOnlyResultOptions = new MesPluginOptions
        {
            Values = new Dictionary<string, string>(databaseOnly.Values) { ["EnableCsv"] = "false", ["EnableCsvRelation"] = "false" }
        };
        var recheckCallsBeforeDatabaseOnly = Volatile.Read(ref recheckCalls);
        Check((await databaseOnlyPlugin.UploadResultAsync(databaseOnlyResultOptions, AdditionalRequest("JW_DATABASE_ONLY", 3))).Code == "PENDING_RECHECK", "关闭 Web API 后仍写检测表并标记未复判");
        Check(Volatile.Read(ref recheckCalls) == recheckCallsBeforeDatabaseOnly, "关闭 Web API 后不调用复判接口");
        Check(await CountAsync("pc_insph_data") == 5, "人工补写和只写数据库模式的结果数量");
        var noScanFlow = new TestFlowService();
        var noScanOptions = TestFlowOptions.ForNoScan();
        noScanFlow.Initialize(noScanOptions);
        noScanFlow.LoadTestItems("Item1,R1,6,4,Ω;end");
        const string noScanId = "NOSCAN20261023170245";
        var acceptedNoScan = noScanFlow.AcceptBarcode(noScanId, noScanOptions);
        Check(acceptedNoScan.State == TestFlowState.Ready && acceptedNoScan.Barcode == noScanId, "不扫码模式保留生成的完整编号，不套用默认条码长度");
        noScanFlow.StartTest();
        var noScanSnapshot = noScanFlow.CompleteTest("Test1,7.01,NG;end");
        var noScanConfig = new AppConfiguration();
        noScanConfig.User.NoScanMode = true;
        noScanConfig.Output.SinglePieceCsvEnabled = true;
        noScanConfig.Output.SinglePieceCsvDirectory = Path.Combine(journal, "single");
        noScanConfig.Output.CsvDirectory = Path.Combine(journal, "daily");
        noScanConfig.Output.CsvName = "JW_NOSCAN";
        var noScanContext = new ResultExportContext { Configuration = noScanConfig, Snapshot = noScanSnapshot };
        var noScanCsv = await new SinglePieceCsvResultExporter().ExportAsync(noScanContext);
        var noScanDailyCsv = await new CsvResultExporter().ExportAsync(noScanContext);
        Check(noScanCsv.IsSuccess && noScanDailyCsv.IsSuccess, "不扫码 FAIL 同时生成每日及单片 CSV");
        Check((await File.ReadAllTextAsync(noScanCsv.FilePath)).Contains(noScanId), "单片 CSV 中保留本机测试编号");
        Check((await File.ReadAllTextAsync(noScanDailyCsv.FilePath)).Contains(noScanId), "每日 CSV 实际写入测试数据而非只生成空文件");
        noScanConfig.Mes.PluginOptions["Product"] = "JW-P1";
        noScanConfig.Mes.PluginOptions["SpecCode"] = "ICT";
        var jwContext = new ResultExportContext { Configuration = noScanConfig, Snapshot = noScanSnapshot, IsJingWangAccount = true };
        var jwPiece = await new SinglePieceCsvResultExporter().ExportAsync(jwContext);
        var jwDaily = await new CsvResultExporter().ExportAsync(jwContext);
        var singleLines = await File.ReadAllLinesAsync(jwPiece.FilePath);
        Check(Path.GetFileName(jwDaily.FilePath) == $"JW-P1-ICT-{jwContext.EndTime:yyyyMMdd}.csv", "景旺汇总按 MES 料号、工序和日期命名");
        Check(singleLines.SequenceEqual(await File.ReadAllLinesAsync(jwDaily.FilePath)), "景旺汇总与单片 CSV 首笔逐行相同");
        Check(singleLines.Last().Split(',')[2] == "JW-P1" && singleLines.Last().Split(',')[10] == "ICT", "单片料号、工序使用 MES 字段");
        await new CsvResultExporter().ExportAsync(jwContext);
        var appended = await File.ReadAllLinesAsync(jwDaily.FilePath);
        Check(appended.Length == singleLines.Length + 1 && appended.Last() == singleLines.Last(), "汇总第二笔仅追加数据");
        var oldUsers = new UserConfiguration { Accounts = [] };
        oldUsers.EnsureDefaultAccounts();
        Check(oldUsers.Accounts.Any(a => a.UserName == "jingwang" && a.Password == "jingwang" && a.Role == UserRole.Administrator), "已有配置补建景旺管理员");
        var noScanUpload = new MesUploadRequest
        {
            Barcode = noScanSnapshot.Barcode, Result = "FAIL", StartTime = noScanSnapshot.StartTime!.Value,
            EndTime = noScanSnapshot.EndTime!.Value, PrecheckStatus = "未过站",
            TestItems = noScanSnapshot.Items.Select(item => new MesTestItem
            {
                Name = item.ItemName, Value = item.TestValue, Unit = item.Unit, Result = item.Result,
                LowerLimit = item.LowerLimit, UpperLimit = item.UpperLimit
            }).ToArray(),
            Files = [new MesUploadFile { FilePath = noScanCsv.FilePath }]
        };
        Check((await databaseOnlyPlugin.UploadResultAsync(databaseOnly, noScanUpload)).IsSuccess, "不扫码 FAIL 在关闭 Web API 时写入检测和文件四张表");
        await CheckGuidRelationsAsync(noScanId, noScanCsv.FilePath, "0");
        Check(await CountAsync("pc_insph_data") == 6 && await CountAsync("pc_img_data") == 3 && await CountAsync("pc_img_rel_data") == 3,
            "不扫码 FAIL 的结果和文件记录提交成功");
        Check((await plugin.EndShiftAsync(options)).IsSuccess && await CountAsync("pc_data_emp") == 2, "员工上下机");
        var disabledAfterLogin = new MesPluginOptions { Values = new Dictionary<string, string>(values) { ["EnableEmployee"] = "false" } };
        Check((await plugin.LoginAsync(options, new MesCheckRequest())).IsSuccess, "再次启用员工上机");
        Check((await plugin.EndShiftAsync(disabledAfterLogin)).IsSuccess && plugin.CurrentEmployeeNo == "", "关闭员工功能后清理已有会话");
        Check(await CountAsync("pc_data_emp") == 3, "关闭员工功能后不追加下机记录");
        await JingWangAuditCases.RunAsync(connection, schema, options, journal, Check,
            () => Volatile.Read(ref stationCalls), () => Volatile.Read(ref loginCalls), () => Volatile.Read(ref recheckCalls));
        foreach (var table in tables)
        {
            tableCounts[table] = await CountAsync(table);
            await using (var cmd = new MySqlCommand("SELECT COLUMN_NAME,COLUMN_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA='pc_eap' AND TABLE_NAME=@table ORDER BY ORDINAL_POSITION", connection))
            {
                cmd.Parameters.AddWithValue("@table", table);
                await using var reader = await cmd.ExecuteReaderAsync();
                var columns = new Dictionary<string, string>();
                while (await reader.ReadAsync()) columns[reader.GetString(0)] = reader.GetString(1);
                columnTypes[table] = columns;
            }
            await using (var cmd = new MySqlCommand($"SELECT * FROM `{schema}`.`{table}` ORDER BY ID DESC LIMIT 1", connection))
            {
                await using var reader = await cmd.ExecuteReaderAsync();
                var row = new Dictionary<string, string>();
                if (await reader.ReadAsync())
                    for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? "NULL" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture) ?? "";
                sampleRows[table] = row;
            }
        }
        Check(Directory.EnumerateFiles(journal, "*.json", SearchOption.AllDirectories)
            .All(file => !File.ReadAllText(file).Contains(password, StringComparison.Ordinal)), "独立发送记录中不包含数据库密码");
        Console.WriteLine($"景旺测试通过：{checks.Count} 项断言；8 张目标表及 3 个模拟 API 已验证。");
        foreach (var pair in tableCounts) Console.WriteLine($"{pair.Key}: {pair.Value} 条测试记录");
    }
    finally { await app.StopAsync(); await app.DisposeAsync(); }
}
finally
{
    await ExecuteAsync($"DROP DATABASE IF EXISTS `{schema}`");
    // 测试日志故意保留在临时目录，便于排查失败用例；正式运行目录不受影响。
}

if (reportPath is not null)
{
    Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
    await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new
    {
        StartedAt = startedAt, FinishedAt = DateTimeOffset.Now, MySqlVersion = connection.ServerVersion,
        SourceSchema = "pc_eap", ScratchSchema = schema, ScratchSchemaDropped = true,
        TestJournal = journal, Status = "PASS", AssertionCount = checks.Count, TableCounts = tableCounts,
        ActualColumnTypes = columnTypes, SampleRows = sampleRows, Assertions = checks
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"测试报告：{reportPath}");
}

async Task ExecuteAsync(string sql)
{
    await using var command = new MySqlCommand(sql, connection);
    await command.ExecuteNonQueryAsync();
}

async Task<int> CountAsync(string table)
{
    await using var command = new MySqlCommand($"SELECT COUNT(*) FROM `{schema}`.`{table}`", connection);
    return Convert.ToInt32(await command.ExecuteScalarAsync());
}

async Task CheckGuidRelationsAsync(string barcode, string csvPath, string originalResult)
{
    await using var command = new MySqlCommand($"""
        SELECT h.DATAGUID, r.DATAGUID, r.IMGGUID, i.IMGGUID,
               (SELECT COUNT(*) FROM `{schema}`.pc_insp_data d WHERE d.DATAGUID=h.DATAGUID) AS DetailCount
        FROM `{schema}`.pc_insph_data h
        INNER JOIN `{schema}`.pc_img_rel_data r ON r.DATAGUID=h.DATAGUID
        INNER JOIN `{schema}`.pc_img_data i ON i.IMGGUID=r.IMGGUID
        WHERE h.PCSID=@barcode AND h.PASSYN='0' AND h.CurrStationYN=@ng AND i.IMGCONTENT=CONCAT('ICT/',i.IMGGUID) AND i.FILETYPE='CSV'
        """, connection);
    command.Parameters.AddWithValue("@barcode", barcode);
    command.Parameters.AddWithValue("@ng", originalResult == "1" ? "0" : "1");

    await using var reader = await command.ExecuteReaderAsync();
    Check(await reader.ReadAsync(), "通过 DATAGUID 和 IMGGUID 找到对应测试的 CSV");
    Check(Guid.TryParse(reader.GetString(0), out _) && Guid.TryParse(reader.GetString(2), out _), "测试和文件的标识均为有效 GUID");
    Check(reader.GetString(0) == reader.GetString(1) && reader.GetString(2) == reader.GetString(3), "两组关联键一致");
    var expectedBytes = await File.ReadAllBytesAsync(csvPath);
    Check(uploaded.TryGetValue($"ICT/{reader.GetString(3)}.csv", out var actualBytes) && expectedBytes.SequenceEqual(actualBytes), "S3 对象键及 CSV 内容正确");
    Check(reader.GetInt64(4) == (originalResult == "1" ? 0 : 1), "同一 DATAGUID 仅关联不良明细");
    Check(!await reader.ReadAsync(), "每笔测试准确关联自身 CSV，未重复或串联");
}

void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException("验证失败：" + label);
    checks.Add(label);
}
