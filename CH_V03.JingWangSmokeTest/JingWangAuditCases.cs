using CH_V03.Core.Mes;
using CH_V03.Mes.JingWang;
using MySqlConnector;
using System.Globalization;
using System.Text.Json;

internal static class JingWangAuditCases
{
    public static async Task RunAsync(MySqlConnection db, string schema, MesPluginOptions original, string journal,
        Action<bool, string> check, Func<int> stationCalls, Func<int> loginCalls, Func<int> recheckCalls)
    {
        await db.ChangeDatabaseAsync(schema);
        var resultTables = new[] { "pc_insp_data", "pc_insph_data", "pc_img_data", "pc_img_rel_data" };
        var csv = Path.Combine(journal, "audit_piece.csv");
        await File.WriteAllTextAsync(csv, "SN,Result\nAUDIT,PASS\n");
        var sequence = 0;
        MesPluginOptions Options(string name, params (string Key, string Value)[] overrides)
        {
            var values = new Dictionary<string, string>(original.Values, StringComparer.OrdinalIgnoreCase)
            {
                ["JournalDirectory"] = Path.Combine(journal, "audit", name), ["EnableWebApi"] = "false", ["OnlineMode"] = "false"
            };
            foreach (var pair in overrides) values[pair.Key] = pair.Value;
            return new() { Values = values };
        }
        MesUploadRequest Request(string barcode, string result = "PASS", string? file = null, string precheck = "已过站")
        {
            var end = DateTime.Now.AddSeconds(++sequence);
            end = new DateTime(end.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond);
            return new()
            {
                Barcode = barcode, UserNo = "AUDIT_ORIGINAL_EMP", Result = result, PrecheckStatus = precheck,
                StartTime = end.AddSeconds(-3), EndTime = end,
                Files = [new MesUploadFile { FilePath = file ?? csv }],
                TestItems = [new MesTestItem { Name = "R1", Value = "5.01", Result = "OK", Unit = "Ω", LowerLimit = "4", UpperLimit = "6" },
                    new MesTestItem { Name = "R2", Value = "8.01", Result = "NG", Unit = "Ω", LowerLimit = "4", UpperLimit = "6" }]
            };
        }
        async Task<string> Scalar(string sql, params (string Key, object Value)[] parameters)
        {
            await using var cmd = new MySqlCommand(sql.Replace("{db}", $"`{schema}`", StringComparison.Ordinal), db);
            foreach (var pair in parameters) cmd.Parameters.AddWithValue(pair.Key, pair.Value);
            return Convert.ToString(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture) ?? "";
        }
        async Task<int> Count(string table) => int.Parse(await Scalar($"SELECT COUNT(*) FROM {{db}}.`{table}`"), CultureInfo.InvariantCulture);
        async Task<int[]> Counts()
        {
            var result = new List<int>();
            foreach (var table in resultTables) result.Add(await Count(table));
            return result.ToArray();
        }
        async Task Execute(string sql, params (string Key, object Value)[] parameters)
        {
            await using var cmd = new MySqlCommand(sql.Replace("{db}", $"`{schema}`", StringComparison.Ordinal), db);
            foreach (var pair in parameters) cmd.Parameters.AddWithValue(pair.Key, pair.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        var defaults = new JingWangMesPlugin().GetConfigFields();
        check(defaults.Single(item => item.Key == "CraftMinutes").DefaultValue == "5", "工艺默认周期为 5 分钟");
        check(defaults.Single(item => item.Key == "ScanType").DefaultValue == "PCSID", "读码默认类型为 PCSID");
        var started = new DateTime(2026, 10, 29, 8, 0, 0);
        var schedule = new JingWangCraftSchedule(started);
        var sampleEnd = started.AddSeconds(10);
        check(!schedule.TryBegin(started.AddMinutes(5).AddMilliseconds(-1), sampleEnd, 5), "工艺周期不足 5 分钟不采集");
        check(schedule.TryBegin(started.AddMinutes(5), sampleEnd, 5), "工艺到 5 分钟边界允许采集");
        check(!schedule.TryBegin(started.AddMinutes(10), sampleEnd.AddSeconds(1), 5), "工艺采集进行中不并发采集");
        schedule.Complete(sampleEnd, true);
        check(!schedule.TryBegin(started.AddMinutes(10), sampleEnd, 5), "没有新测试值不重复上传旧值");
        check(schedule.TryBegin(started.AddMinutes(10), sampleEnd.AddMinutes(1), 5), "下一周期有新值时可以采集");
        schedule.Complete(sampleEnd.AddMinutes(1), true);

        var disabled = Options("disabled", ("EnableScan", "false"), ("EnableAlarm", "false"), ("EnableCraft", "false"), ("EnableEmployee", "false"));
        var disabledPlugin = new JingWangMesPlugin();
        var eventBefore = new[] { await Count("pc_data_scan"), await Count("pc_data_alarm"), await Count("pc_data_craft"), await Count("pc_data_emp") };
        await disabledPlugin.RecordScanAsync(disabled, "JW_DISABLED", "EMP");
        await disabledPlugin.RecordAlarmAsync(disabled, "AUDIT_DISABLED", "关闭", true);
        await disabledPlugin.CollectCraftAsync(disabled, [new MesTestItem { Name = "R1", Value = "1" }], DateTime.Now);
        var loginBefore = loginCalls();
        await disabledPlugin.LoginAsync(disabled, new());
        check(eventBefore.SequenceEqual(new[] { await Count("pc_data_scan"), await Count("pc_data_alarm"), await Count("pc_data_craft"), await Count("pc_data_emp") })
            && loginCalls() == loginBefore, "关闭扫码、报警、工艺及员工功能后不写表、不调用上机接口");

        foreach (var disabledKey in new[] { "EnableInspection", "EnableInspectionResult", "EnableCsv", "EnableCsvRelation" })
        {
            var options = Options(disabledKey, (disabledKey, "false"));
            if (disabledKey == "EnableCsv") options.Values["EnableCsvRelation"] = "false";
            var before = await Counts();
            check((await new JingWangMesPlugin().UploadResultAsync(options, Request("JW_SWITCH_" + disabledKey))).IsSuccess, disabledKey + " 关闭时其他功能正常写入");
            var after = await Counts();
            var expected = new[] { disabledKey == "EnableInspection" ? 0 : 1, disabledKey == "EnableInspectionResult" ? 0 : 1,
                disabledKey == "EnableCsv" ? 0 : 1, disabledKey is "EnableCsv" or "EnableCsvRelation" ? 0 : 1 };
            check(after.Zip(before, (a, b) => a - b).SequenceEqual(expected), disabledKey + " 对应表未新增，其他启用表数量正确");
        }

        var exactOptions = Options("exact");
        var exactRequest = Request("JW_EXACT", "FAIL");
        var exactPlugin = new JingWangMesPlugin();
        check((await exactPlugin.UploadResultAsync(exactOptions, exactRequest)).IsSuccess, "FAIL 原始结论正常写表");
        check(await Scalar("SELECT CONCAT(WONO,'|',SPECCODE,'|',PRODUCT,'|',CONTAINER,'|',PASSYN,'|',CurrStationYN,'|',EMPNO) FROM {db}.pc_insph_data WHERE PCSID=@sn",
            ("@sn", exactRequest.Barcode)) == "JW_WO|JW_SPEC|JW_PRODUCT|JW_LOT|0|1|AUDIT_ORIGINAL_EMP", "检测结果的工单、工序、料号、批次、原始结论及测试员工正确");
        check(await Scalar("SELECT DATE_FORMAT(CREATETIME,'%Y-%m-%d %H:%i:%s') FROM {db}.pc_insph_data WHERE PCSID=@sn", ("@sn", exactRequest.Barcode))
            == exactRequest.EndTime.ToString("yyyy-MM-dd HH:mm:ss"), "数据库检测时间与本次测试结束时间一致");
        check(await Scalar("SELECT CONCAT(MINVAL,'|',MAXVAL,'|',VAL,'|',UOM,'|',PASSYN) FROM {db}.pc_insp_data WHERE PARENTID=@sn AND INSPITENCODE='BAN_SEQ_NO2' AND INSPITEMDES='R2'",
            ("@sn", exactRequest.Barcode)) == "4|6|8.01|Ω|0", "检测明细限值、实测值、单位及 NG 判定正确");
        check(await Scalar("SELECT COUNT(*) FROM {db}.pc_insp_data d JOIN {db}.pc_insph_data h ON d.DATAGUID=h.DATAGUID JOIN {db}.pc_img_rel_data r ON r.DATAGUID=h.DATAGUID JOIN {db}.pc_img_data i ON i.IMGGUID=r.IMGGUID WHERE h.PCSID=@sn AND i.IMGCONTENT=CONCAT('ICT/',i.IMGGUID) AND i.FILETYPE='CSV'",
            ("@sn", exactRequest.Barcode)) == "1", "仅不良明细通过 DATAGUID 关联单片 CSV");

        check(await Scalar("SELECT COUNT(*) FROM {db}.pc_insp_data WHERE PARENTID=@sn AND INSPITENCODE='BAN_SEQ_NO1' AND INSPITEMDES='R1' AND PASSYN='0'",
            ("@sn", exactRequest.Barcode)) == "0", "混合结果中排除通过项");
        foreach (var kind in new[] { "missing-production", "missing-file", "long-path", "non-csv", "invalid-switch" })
        {
            var options = Options(kind);
            var file = csv;
            if (kind == "missing-production") options.Values["WorkOrder"] = "";
            if (kind == "missing-file") file = Path.Combine(journal, "not-created.csv");
            if (kind == "long-path") options.Values["S3Prefix"] = new string('x', 240);
            if (kind == "non-csv") file = Path.Combine(journal, "wrong.txt");
            if (kind == "invalid-switch") options.Values["EnableCsv"] = "false";
            var before = await Counts();
            var plugin = new JingWangMesPlugin();
            check(!(await plugin.UploadResultAsync(options, Request("JW_INVALID_" + kind, file: file))).IsSuccess, kind + " 在保存前被拒绝");
            check(before.SequenceEqual(await Counts()) && plugin.GetPendingRecords(options).Count == 1, kind + " 不产生部分记录并保留待配置项");
        }

        var transactionOptions = Options("transaction");
        var transactionRequest = Request("JW_ROLLBACK", "FAIL");
        var transactionPlugin = new JingWangMesPlugin();
        var countsBeforeRollback = await Counts();
        await Execute("CREATE TRIGGER {db}.jw_audit_block_relation BEFORE INSERT ON {db}.pc_img_rel_data FOR EACH ROW SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='audit_rollback'");
        try
        {
            check(!(await transactionPlugin.UploadResultAsync(transactionOptions, transactionRequest)).IsSuccess, "在关系表写入时注入数据库故障");
            check(countsBeforeRollback.SequenceEqual(await Counts()), "最后一张表失败时检测明细、结果和文件三张表全部回滚");
        }
        finally { await Execute("DROP TRIGGER IF EXISTS {db}.jw_audit_block_relation"); }
        var pendingTransaction = transactionPlugin.GetPendingRecords(transactionOptions).Single();
        var changedOrder = new MesPluginOptions { Values = new(transactionOptions.Values) };
        changedOrder.Values["WorkOrder"] = "ANOTHER_ORDER";
        changedOrder.Values["Container"] = "ANOTHER_LOT";
        check((await new JingWangMesPlugin().RetryPendingDatabaseAsync(changedOrder, pendingTransaction.Id)).IsSuccess, "故障恢复后人工补写整笔事务");
        check(await Scalar("SELECT CONCAT(WONO,'|',CONTAINER) FROM {db}.pc_insph_data WHERE PCSID=@sn", ("@sn", transactionRequest.Barcode)) == "JW_WO|JW_LOT",
            "切换工单后补写仍使用原工单及原批次");
        check((await transactionPlugin.UploadResultAsync(transactionOptions, transactionRequest)).Code == "DUPLICATE", "补写成功后重复提交不会再插入");

        var dataId = await Scalar("SELECT DATAGUID FROM {db}.pc_insph_data WHERE PCSID=@sn", ("@sn", transactionRequest.Barcode));
        await Execute("DELETE i FROM {db}.pc_img_data i JOIN {db}.pc_img_rel_data r ON i.IMGGUID=r.IMGGUID WHERE r.DATAGUID=@id", ("@id", dataId));
        await Execute("DELETE FROM {db}.pc_img_rel_data WHERE DATAGUID=@id", ("@id", dataId));
        await Execute("DELETE FROM {db}.pc_insp_data WHERE DATAGUID=@id", ("@id", dataId));
        await Execute("DELETE FROM {db}.pc_insph_data WHERE DATAGUID=@id", ("@id", dataId));
        var countsAfterConsumption = await Counts();
        check((await new JingWangMesPlugin().UploadResultAsync(transactionOptions, transactionRequest)).Code == "DUPLICATE"
            && countsAfterConsumption.SequenceEqual(await Counts()), "模拟 EAP 取后删表，重启插件后仍靠本机记录防重复");

        foreach (var kind in new[] { "SCAN", "ALARM", "CRAFT", "EMP" })
        {
            var options = Options("event-" + kind, ("DbPort", "1"), ("EnableWebApi", "true"));
            var plugin = new JingWangMesPlugin();
            var collected = DateTime.Now;
            var items = new[] { new MesTestItem { Name = "R1", Value = "5.01", Result = "OK", Unit = "Ω" } };
            var result = kind switch
            {
                "SCAN" => await plugin.RecordScanAsync(options, "JW_EVENT_SCAN", "EMP"),
                "ALARM" => await plugin.RecordAlarmAsync(options, "AUDIT_ALARM", "测试报警", true),
                "CRAFT" => await plugin.CollectCraftAsync(options, items, collected),
                _ => await plugin.LoginAsync(options, new())
            };
            check(!result.IsSuccess && result.Code == "PENDING_DATABASE", kind + " 断库时持久保存待写入事件");
            if (kind == "CRAFT")
                check((await plugin.CollectCraftAsync(options, items, collected)).Code == "PENDING_DATABASE" && plugin.GetPendingRecords(options).Count == 1,
                    "工艺断库重复采集同一测试只保留一条待写入，恢复后不自动重放");
            var pending = new JingWangMesPlugin().GetPendingRecords(options).Single();
            options.Values["DbPort"] = original.Get("DbPort");
            var table = kind switch { "SCAN" => "pc_data_scan", "ALARM" => "pc_data_alarm", "CRAFT" => "pc_data_craft", _ => "pc_data_emp" };
            var before = await Count(table);
            check((await new JingWangMesPlugin().RetryPendingDatabaseAsync(options, pending.Id)).IsSuccess && await Count(table) == before + 1,
                kind + " 重启后人工补写到对应表");
            if (kind == "CRAFT")
                check((await plugin.CollectCraftAsync(options, items, collected)).Code == "DUPLICATE" && await Count(table) == before + 1,
                    "同一份工艺值补写成功后不再重复写入");
        }
        check(await Scalar("SELECT DRCTYPE FROM {db}.pc_data_scan WHERE PNLIDORSETID='JW_EVENT_SCAN'") == "PCSID", "读码类型实际落库为 PCSID");
        check(await Scalar("SELECT CONCAT(ITEMCODE,'|',ITEMVALUE,'|',ISOK) FROM {db}.pc_data_craft WHERE ITEMCODE='R_CODE' ORDER BY ID DESC LIMIT 1") == "R_CODE|5.01|1",
            "工艺测试项映射及判定实际落库正确");

        var apiOptions = Options("api-retry", ("EnableWebApi", "true"), ("OnlineMode", "true"));
        var apiPlugin = new JingWangMesPlugin();
        var apiRequest = Request("JW_RETRY_API", "FAIL", precheck: "未过站");
        var recheckBefore = recheckCalls();
        check((await apiPlugin.UploadResultAsync(apiOptions, apiRequest)).Code == "PENDING_RECHECK" && recheckCalls() == recheckBefore,
            "过站未通过时允许写表，但不自动复判");
        var apiPending = apiPlugin.GetPendingRecords(apiOptions).Single();
        apiOptions.Values["WorkOrder"] = "ANOTHER_ORDER";
        var stationBefore = stationCalls();
        for (var attempt = 0; attempt < 3; attempt++)
            check((await new JingWangMesPlugin().RetryPendingApisAsync(apiOptions, apiPending.Id)).IsSuccess, "人工补接口尝试 " + (attempt + 1));
        check(stationCalls() == stationBefore + 1 && recheckCalls() == recheckBefore + 3,
            "复判失败再重试时保留已过站状态，不重复调用过站接口");
        check(!apiPlugin.GetPendingRecords(apiOptions).Any(), "人工补接口成功后移出待处理列表");
        check(Directory.EnumerateFiles(Path.Combine(apiOptions.Get("JournalDirectory"), "api-history"), "*.json", SearchOption.AllDirectories).Count() == 3,
            "三次复判结果均独立留档，包括最终成功结果");
        check(await Scalar("SELECT PASSYN FROM {db}.pc_insph_data WHERE PCSID='JW_RETRY_API'") == "0", "复判 OK 不覆盖原始 FAIL");

        foreach (var sn in new[] { "JW_MISMATCH", "JW_TIMEOUT", "JW_API_UNAVAILABLE" })
        {
            var options = Options(sn, ("EnableWebApi", "true"), ("OnlineMode", "true"), ("ApiTimeoutSeconds", "1"));
            if (sn == "JW_API_UNAVAILABLE") options.Values["RecheckUrl"] = "http://127.0.0.1:1/recheck";
            var plugin = new JingWangMesPlugin();
            check((await plugin.UploadResultAsync(options, Request(sn))).Code == "PENDING_RECHECK" && plugin.GetPendingRecords(options).Count == 1,
                sn + " 接口结果异常时保留未复判，检测数据仍写表");
        }
        var s3Options = Options("s3-retry", ("S3Bucket", "unavailable"));
        var s3Request = Request("JW_S3_RETRY");
        var s3Plugin = new JingWangMesPlugin();
        var beforeFailure = await Counts();
        check(!(await s3Plugin.UploadResultAsync(s3Options, s3Request)).IsSuccess && beforeFailure.SequenceEqual(await Counts()),
            "S3 上传失败时数据库事务回滚");
        var pendingCsv = s3Plugin.GetPendingRecords(s3Options).Single();
        s3Options.Values["S3Bucket"] = original.Get("S3Bucket");
        s3Options.Values["S3Prefix"] = "";
        check((await s3Plugin.RetryPendingDatabaseAsync(s3Options, pendingCsv.Id)).IsSuccess, "S3 配置修正后可补传");
        check(await Scalar("SELECT COUNT(*) FROM {db}.pc_img_data i JOIN {db}.pc_img_rel_data r ON i.IMGGUID=r.IMGGUID JOIN {db}.pc_insph_data h ON h.DATAGUID=r.DATAGUID WHERE h.PCSID='JW_S3_RETRY' AND i.IMGCONTENT=i.IMGGUID") == "1", "空文件夹直接保存文件标识");

        var employeeOptions = Options("employee", ("EnableWebApi", "true"));
        var employeePlugin = new JingWangMesPlugin();
        check((await employeePlugin.LoginAsync(employeeOptions, new())).IsSuccess, "员工许可通过后写入 INTIME");
        var callsBeforeLogout = loginCalls();
        check(await Scalar("SELECT COUNT(*) FROM {db}.pc_data_emp WHERE EMPCODE='INTIME' AND EMPDES='上机时间' AND EMPTIME IS NOT NULL") != "0", "员工上机描述和时间已落库");
        employeeOptions.Values["LoginUrl"] = "http://127.0.0.1:1/login";
        check((await employeePlugin.EndShiftAsync(employeeOptions)).IsSuccess && loginCalls() == callsBeforeLogout,
            "下机只写 OUTTIME，不调用上机或下机 Web API");
        check(await Scalar("SELECT COUNT(*) FROM {db}.pc_data_emp WHERE EMPCODE='OUTTIME' AND EMPDES='下机时间' AND EMPTIME IS NOT NULL") != "0", "员工 OUTTIME 已实际落库");
        employeeOptions.Values["LoginUrl"] = original.Get("LoginUrl");
        check((await employeePlugin.LoginAsync(employeeOptions, new())).IsSuccess, "员工重新上机可建立新会话");
        employeeOptions.Values["EmployeeCardCode"] = "DENY";
        check(!(await employeePlugin.LoginAsync(employeeOptions, new())).IsSuccess && employeePlugin.CurrentEmployeeNo == "",
            "上机被拒绝后不沿用上一次员工会话");
    }
}
