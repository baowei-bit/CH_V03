using System.Globalization;
using System.Text;
using System.Text.Json;
using CH_V03.Core.Mes;
using MySqlConnector;

namespace CH_V03.Mes.JingWang;

public sealed class JingWangMesPlugin : IJingWangMesPlugin
{
    public const string PluginId = "JingWang.Eap";
    private static readonly HttpClient Http = new();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private string _employeeNo = string.Empty;
    private string _employeeName = string.Empty;

    public string CurrentEmployeeNo => _employeeNo;

    public string Id => PluginId;
    public string Name => "景旺 EAP（本地 MySQL）";
    public MesPluginCapabilities Capabilities { get; } = new()
    {
        SupportsConnectionTest = true, SupportsLogin = true, SupportsPreCheck = true,
        SupportsResultUpload = true, SupportsOfflineCache = true
    };

    public IReadOnlyList<MesPluginConfigField> GetConfigFields() =>
    [
        Field("DbHost", "数据库地址", "127.0.0.1", true),
        Field("DbPort", "端口", "3306", true),
        Field("DbName", "数据库名", "pc_eap", true),
        Field("DbUser", "数据库用户", "root", true),
        Field("EquipmentCode", "设备编码", required: true),
        Field("EmployeeCardCode", "员工卡号/工号", required: true),
        Field("WorkOrder", "工单号", required: true),
        Field("SpecCode", "工序代码", required: true),
        Field("Product", "料号", required: true),
        Field("Container", "批次号", required: true),
        Field("SetId", "SET 码"),
        Field("PanelSequence", "板序", "1", true),
        Field("EnableCraft", "启用工艺质量", "true"),
        Field("CraftMinutes", "采集间隔（分钟）", "5", true),
        Field("CraftMappings", "工艺项映射：测试项=ITEMCODE|描述|单位；多项用分号分隔"),
        Field("EnableScan", "启用设备读码", "true"),
        Field("ScanType", "码类型", "PCSID", true),
        Field("EnableAlarm", "启用设备报警", "true"),
        Field("EnableEmployee", "启用员工上下机", "true"),
        Field("EnableInspection", "启用检测明细", "true"),
        Field("EnableInspectionResult", "启用检测结果", "true"),
        Field("EnableCsv", "启用 CSV 文件记录", "true"),
        Field("EnableCsvRelation", "启用 CSV 与检测关系", "true"),
        Field("S3Endpoint", "S3 服务地址"),
        Field("S3Bucket", "S3 存储桶"),
        Field("S3Prefix", "S3 文件夹（可留空）"),
        Field("S3Region", "S3 区域", "us-east-1"),
        Field("S3AccessKey", "S3 Access Key"),
        Field("EnableWebApi", "启用 Web API", "true"),
        Field("ApiLogDirectory", "MES 接口日志路径", Path.Combine(AppContext.BaseDirectory, "data", "jingwang", "mes-log")),
        Field("ToolLoginUrl", "工治具上机 API 地址", "http://10.51.8.4:8020/api/ToolLife/GetToolLifeQuery"),
        Field("ToolLogoutUrl", "工治具下机 API 地址", "http://10.51.8.4:8020/api/ToolLife/ResourceThruput"),
        Field("LoginUrl", "员工上机 API 地址"),
        Field("StationUrl", "过站 API 地址"),
        Field("RecheckUrl", "复判 API 地址"),
        Field("ApiTimeoutSeconds", "API 超时（秒）", "5", true)
    ];

    public async Task<MesResult> TestConnectionAsync(MesPluginOptions options, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await OpenAsync(options, cancellationToken);
            await using var command = new MySqlCommand("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=@db AND TABLE_NAME IN ('pc_data_craft','pc_data_scan','pc_data_alarm','pc_data_emp','pc_insp_data','pc_insph_data','pc_img_data','pc_img_rel_data')", connection);
            command.Parameters.AddWithValue("@db", options.Get("DbName", "pc_eap"));
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            return count == 8 ? MesResult.Success("本地 MySQL 已连接，8 张目标表均存在") : MesResult.Fail($"数据库已连接，但仅找到 {count}/8 张目标表");
        }
        catch (Exception ex) { return SafeFailure("MySQL 连接失败", ex); }
    }

    public async Task<MesResult> LoginAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        _employeeNo = string.Empty;
        _employeeName = string.Empty;
        if (!Enabled(options, "EnableEmployee"))
        {
            _employeeNo = string.Empty;
            _employeeName = string.Empty;
            return MesResult.Success("员工上下机未启用，已跳过员工上机");
        }
        if (!Enabled(options, "EnableWebApi"))
        {
            _employeeNo = string.Empty;
            _employeeName = string.Empty;
            return MesResult.Success("Web API 未启用，已跳过员工上机接口，仅使用数据库", "SKIPPED");
        }
        var equipment = options.Get("EquipmentCode");
        var card = options.Get("EmployeeCardCode", request.UserNo);
        if (string.IsNullOrWhiteSpace(equipment) || string.IsNullOrWhiteSpace(card)) return MesResult.Fail("设备编码和员工卡号不能为空");
        if (!Uri.TryCreate(options.Get("LoginUrl"), UriKind.Absolute, out var loginUri)
            || (loginUri.Scheme != Uri.UriSchemeHttp && loginUri.Scheme != Uri.UriSchemeHttps))
            return MesResult.Fail("员工上机 API 地址未配置或无效，请在 Web API 页签填写员工上机 API 地址");
        try
        {
            using var document = await PostJsonAsync(options.Get("LoginUrl"), new { EquCode = equipment, CardCode = card, OpCode = "login" }, options, cancellationToken);
            var root = document.RootElement;
            if (Value(root, "Code") != "0" || Value(root, "Permit") != "1")
                return MesResult.Fail("景旺未允许员工上机：" + Value(root, "Message"));
            _employeeNo = Value(root, "EmpNo");
            _employeeName = Value(root, "EmpName");
            if (string.IsNullOrWhiteSpace(_employeeNo)) return MesResult.Fail("员工上机返回缺少 EmpNo");
            if (Enabled(options, "EnableEmployee"))
            {
                var result = await QueueAndWriteEventAsync(options, "EMP", "员工上机",
                    [Row(("no", _employeeNo), ("name", _employeeName), ("code", "INTIME"), ("description", "上机时间"),
                        ("time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))], cancellationToken);
                if (!result.IsSuccess)
                {
                    _employeeNo = string.Empty;
                    _employeeName = string.Empty;
                    return result;
                }
            }
            return MesResult.Success($"景旺员工上机成功：{_employeeNo}");
        }
        catch (Exception ex) { _employeeNo = string.Empty; return SafeFailure("员工上机失败", ex); }
    }

    public async Task<MesResult> EndShiftAsync(MesPluginOptions options, CancellationToken cancellationToken = default)
    {
        if (!Enabled(options, "EnableEmployee"))
        {
            _employeeNo = string.Empty;
            _employeeName = string.Empty;
            return MesResult.Success("员工上下机未启用，已跳过员工下机记录");
        }
        if (string.IsNullOrWhiteSpace(_employeeNo)) return MesResult.Success("无景旺上机会话");
        var no = _employeeNo;
        try
        {
            if (Enabled(options, "EnableEmployee"))
            {
                var result = await QueueAndWriteEventAsync(options, "EMP", "员工下机",
                    [Row(("no", no), ("name", _employeeName), ("code", "OUTTIME"), ("description", "下机时间"),
                        ("time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))], cancellationToken);
                if (!result.IsSuccess) return result;
            }
            _employeeNo = string.Empty;
            _employeeName = string.Empty;
            return MesResult.Success($"已写入员工下机：{no}");
        }
        catch (Exception ex) { return SafeFailure("写入下机记录失败", ex); }
    }

    public async Task<MesResult> CheckBeforeTestAsync(MesPluginOptions options, MesCheckRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enabled(options, "EnableWebApi")) return MesResult.Success("Web API 未启用，已跳过过站接口，本次记录未过站", "SKIPPED");
        try
        {
            using var document = await PostJsonAsync(options.Get("StationUrl"), new
            {
                equipmentCode = options.Get("EquipmentCode"), specCode = options.Get("SpecCode"), sn = request.Barcode,
                woCode = options.Get("WorkOrder"), empNo = First(_employeeNo, request.UserNo), lotNo = options.Get("Container")
            }, options, cancellationToken);
            var root = document.RootElement;
            return Value(root, "Result") == "1"
                ? MesResult.Success("景旺过站校验通过", rawResponse: root.GetRawText())
                : MesResult.Fail("景旺过站校验未通过：" + Value(root, "Message"), rawResponse: root.GetRawText());
        }
        catch (Exception ex) { return SafeFailure("景旺过站校验失败", ex); }
    }

    public async Task<MesResult> RecordScanAsync(MesPluginOptions options, string barcode, string employeeNo, CancellationToken cancellationToken = default)
    {
        if (!Enabled(options, "EnableScan")) return MesResult.Success("读码记录未启用");
        if (string.IsNullOrWhiteSpace(barcode)) return MesResult.Fail("读码记录缺少条码");
        return await QueueAndWriteEventAsync(options, "SCAN", barcode,
            [Row(("lot", options.Get("Container")), ("barcode", barcode), ("type", options.Get("ScanType", "PCSID")),
                ("parent", barcode), ("user", First(_employeeNo, employeeNo)),
                ("time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))], cancellationToken);
    }

    public async Task<MesResult> RecordAlarmAsync(MesPluginOptions options, string code, string description, bool active, CancellationToken cancellationToken = default)
    {
        if (!Enabled(options, "EnableAlarm")) return MesResult.Success("报警记录未启用");
        if (string.IsNullOrWhiteSpace(code)) return MesResult.Fail("报警代码不能为空");
        return await QueueAndWriteEventAsync(options, "ALARM", code,
            [Row(("code", code), ("description", description), ("value", active ? "1" : "0"),
                ("time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)))], cancellationToken);
    }

    public async Task<MesResult> CollectCraftAsync(MesPluginOptions options, IReadOnlyList<MesTestItem> items, DateTime collectedAt, CancellationToken cancellationToken = default)
    {
        if (!Enabled(options, "EnableCraft")) return MesResult.Success("工艺质量未启用");
        var mappings = ParseMappings(options.Get("CraftMappings"));
        if (mappings.Count == 0) return MesResult.Fail("未配置工艺项映射");
        var selected = items.Where(item => mappings.ContainsKey(item.Name) && !string.IsNullOrWhiteSpace(item.Value)).ToList();
        if (selected.Count == 0) return MesResult.Success("本周期没有可采集的测试项");
        var group = new DateTimeOffset(collectedAt).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var rows = selected.Select(item =>
        {
            var map = mappings[item.Name];
            return Row(("lot", options.Get("Container")), ("panel", options.Get("SetId")), ("item", map.Code),
                ("description", map.Description), ("unit", map.Unit), ("value", item.Value),
                ("ok", IsPass(item.Result) ? "1" : "0"), ("group", group),
                ("time", collectedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        }).ToArray();
        var eventId = HashId("CRAFT\u001f" + JsonSerializer.Serialize(rows));
        return await QueueAndWriteEventAsync(options, "CRAFT", $"工艺质量 {group}", rows, cancellationToken, eventId);
    }

    public async Task<MesResult> UploadResultAsync(MesPluginOptions options, MesUploadRequest request, CancellationToken cancellationToken = default)
    {
        var recordId = RecordId(request);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            var store = new JingWangRecordStore(options);
            if (store.IsCompleted(recordId)) return MesResult.Success("本次测试记录已写入，跳过重复提交", "DUPLICATE");
            store.SavePending(recordId, request, "待写入", "");
            var validation = ValidateRecord(options, request);
            if (validation is not null)
            {
                store.SavePending(recordId, request, "待配置", validation);
                return MesResult.Fail(validation);
            }
            try
            {
                await using var connection = await OpenAsync(options, cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                if (!await RecordExistsAsync(connection, transaction, recordId, cancellationToken))
                    await InsertResultAsync(connection, transaction, options, request, recordId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                store.MarkCompleted(recordId, request, "已写入数据库");
                store.SaveReview(recordId, request,
                    request.PrecheckStatus == "已过站" ? "未复判" : "未过站/未复判",
                    "检测记录已写入，等待复判结果");
            }
            catch (Exception ex)
            {
                store.SavePending(recordId, request, "待写入", ex.GetType().Name);
                return SafeFailure("检测记录待写入", ex);
            }
            if (!Enabled(options, "EnableWebApi") || !Enabled(options, "OnlineMode"))
            {
                store.SaveReview(recordId, request, "未过站/未复判", "MES 离线或复判接口未启用");
                return MesResult.Success("数据库已写入；复判待人工核对", "PENDING_RECHECK");
            }
            if (request.PrecheckStatus != "已过站")
            {
                store.SaveReview(recordId, request, "未过站/未复判", "本次未过站，请人工核对后决定是否补调用接口");
                return MesResult.Success("数据库已写入；未过站，复判待人工核对", "PENDING_RECHECK");
            }
            return await RecheckAsync(options, request, recordId, store, request.PrecheckStatus, cancellationToken);
        }
        catch (Exception ex) { return SafeFailure("景旺记录处理失败", ex); }
        finally { _writeGate.Release(); }
    }

    public IReadOnlyList<JingWangPendingRecord> GetPendingRecords(MesPluginOptions options) =>
        new JingWangRecordStore(options).GetPending().Concat(new JingWangEventStore(options).GetPending())
            .OrderByDescending(item => item.CreatedAt).ToArray();

    public MesResult ArchivePendingRecord(MesPluginOptions options, string recordId)
    {
        try
        {
            return new JingWangRecordStore(options).Archive(recordId) || new JingWangEventStore(options).Archive(recordId)
                ? MesResult.Success("已归档，原始记录保留在本机")
                : MesResult.Fail("找不到待处理记录");
        }
        catch (Exception ex) { return SafeFailure("归档失败", ex); }
    }

    public async Task<MesResult> RetryPendingDatabaseAsync(MesPluginOptions options, string recordId, CancellationToken cancellationToken = default)
    {
        var events = new JingWangEventStore(options);
        var eventRecord = events.Load(recordId);
        if (eventRecord is not null)
        {
            try
            {
                await ExecuteEventAsync(options, eventRecord, cancellationToken);
                events.Complete(eventRecord);
                return MesResult.Success("待处理事件已补写数据库");
            }
            catch (Exception ex) { return SafeFailure("事件补写失败", ex); }
        }
        var store = new JingWangRecordStore(options);
        var request = store.LoadRequest("pending", recordId);
        if (request is null) return MesResult.Fail("找不到待写入记录");
        var values = new Dictionary<string, string>(options.Values, StringComparer.OrdinalIgnoreCase) { ["OnlineMode"] = "false" };
        foreach (var pair in store.LoadProductionFields("pending", recordId)) values[pair.Key] = pair.Value;
        return await UploadResultAsync(new MesPluginOptions { Values = values }, request, cancellationToken);
    }

    public async Task<MesResult> RetryPendingApisAsync(MesPluginOptions options, string recordId, CancellationToken cancellationToken = default)
    {
        var store = new JingWangRecordStore(options);
        var request = store.LoadRequest("review", recordId);
        if (request is null || !store.IsCompleted(recordId)) return MesResult.Fail("请先完成数据库写入");
        if (!Enabled(options, "EnableWebApi") || !Enabled(options, "OnlineMode")) return MesResult.Fail("请先让景旺 MES 在线");
        var retryValues = new Dictionary<string, string>(options.Values, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in store.LoadProductionFields("review", recordId)) retryValues[pair.Key] = pair.Value;
        options = new MesPluginOptions { Values = retryValues };
        store = new JingWangRecordStore(options);
        var precheck = store.LoadPrecheckStatus(recordId) ?? request.PrecheckStatus;
        if (precheck != "已过站")
        {
            var result = await CheckBeforeTestAsync(options, new MesCheckRequest { Barcode = request.Barcode, UserNo = request.UserNo }, cancellationToken);
            if (!result.IsSuccess) return MesResult.Fail("补过站未通过：" + result.Message);
            precheck = "已过站";
            store.SaveReview(recordId, request, "未复判", "人工补过站已通过，等待复判", precheck);
        }
        return await RecheckAsync(options, request, recordId, store, precheck, cancellationToken);
    }

    private async Task<MesResult> RecheckAsync(MesPluginOptions options, MesUploadRequest request, string recordId,
        JingWangRecordStore store, string precheckStatus, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await PostJsonAsync(options.Get("RecheckUrl"), new[]
            {
                new { sn = request.Barcode, product = options.Get("Product"), woCode = options.Get("WorkOrder"),
                    specCode = options.Get("SpecCode"), detectResult = IsPass(request.Result),
                    detectTime = request.EndTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    equipmentCode = options.Get("EquipmentCode"), empNo = First(_employeeNo, request.UserNo) }
            }, options, cancellationToken);
            var root = document.RootElement;
            var status = Value(root, "status");
            var decision = FirstDecision(root, request.Barcode);
            store.SaveApiOutcome(recordId, request, string.IsNullOrWhiteSpace(decision) ? "未复判" : "复判" + decision, root.GetRawText(), precheckStatus);
            if (status != "1" || string.IsNullOrWhiteSpace(decision))
            {
                store.SaveReview(recordId, request, "未复判", root.GetRawText(), precheckStatus);
                return MesResult.Success("数据库已写入；景旺复判待人工核对", "PENDING_RECHECK", rawResponse: root.GetRawText());
            }
            if (precheckStatus == "已过站" && IsPass(decision.Trim()))
                store.ClearReview(recordId);
            else
                store.SaveReview(recordId, request,
                    precheckStatus == "已过站" ? "复判" + decision : "未过站/复判" + decision,
                    root.GetRawText(), precheckStatus);
            var decisionCode = decision.Trim().ToUpperInvariant() switch
            {
                "OK" or "PASS" => "JINGWANG_OK",
                "RETEST" => "JINGWANG_RETEST",
                _ => "JINGWANG_REVIEW"
            };
            return MesResult.Success($"数据库已写入；景旺复判：{decision}", decisionCode, rawResponse: root.GetRawText());
        }
        catch (Exception ex)
        {
            store.SaveApiOutcome(recordId, request, "未复判", ex.GetType().Name, precheckStatus);
            store.SaveReview(recordId, request, "未复判", ex.GetType().Name, precheckStatus);
            return MesResult.Success("数据库已写入；景旺复判待人工核对", "PENDING_RECHECK");
        }
    }

    private async Task InsertResultAsync(MySqlConnection connection, MySqlTransaction transaction, MesPluginOptions options,
        MesUploadRequest request, string id, CancellationToken cancellationToken)
    {
        var time = request.EndTime == default ? DateTime.Now : request.EndTime;
        var isPass = IsPass(request.Result);
        if (Enabled(options, "EnableInspection"))
        {
            var group = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            for (var index = 0; index < request.TestItems.Count; index++)
            {
                var item = request.TestItems[index];
                var result = item.Result?.Trim();
                if (!string.Equals(result, "NG", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(result, "FAIL", StringComparison.OrdinalIgnoreCase))
                    continue;
                await ExecuteAsync(connection, transaction,
                    "INSERT INTO pc_insp_data (PRODUCT,CONTAINER,INSPITENCODE,INSPITEMDES,PARENTID,PNLIDORSETID,MINVAL,MAXVAL,VAL,PASSYN,UOM,BAD_CAUSE_CODE,UserName,DATAGUID,GROUPNUM,CREATETIME) VALUES (@product,@lot,@code,@description,@parent,@panel,@min,@max,@value,@pass,@unit,@cause,@user,@guid,@group,@time)",
                    cancellationToken, ("@product", options.Get("Product")), ("@lot", options.Get("Container")), ("@code", $"BAN_SEQ_NO{index + 1}"),
                    ("@description", item.Name), ("@parent", request.Barcode), ("@panel", options.Get("SetId")),
                    ("@min", item.LowerLimit), ("@max", item.UpperLimit), ("@value", item.Value),
                    ("@pass", "0"), ("@unit", item.Unit),
                    ("@cause", item.Name), ("@user", First(request.UserNo, _employeeNo)),
                    ("@guid", id), ("@group", group), ("@time", time));
            }
        }
        if (Enabled(options, "EnableInspectionResult"))
            await ExecuteAsync(connection, transaction,
                "INSERT INTO pc_insph_data (WONO,SPECCODE,PRODUCT,CONTAINER,SETID,PCSID,BANSEQNO,PASSYN,CurrStationYN,EMPNO,DATAGUID,CREATETIME) VALUES (@wo,@spec,@product,@lot,@set,@pcs,@sequence,@pass,@ng,@employee,@guid,@time)",
                cancellationToken, ("@wo", options.Get("WorkOrder")), ("@spec", options.Get("SpecCode")),
                ("@product", options.Get("Product")), ("@lot", options.Get("Container")), ("@set", options.Get("SetId")),
                ("@pcs", request.Barcode), ("@sequence", options.Get("PanelSequence", "1")), ("@pass", "0"),
                ("@ng", isPass ? "0" : "1"), ("@employee", First(request.UserNo, _employeeNo)),
                ("@guid", id), ("@time", time));
        if (Enabled(options, "EnableCsv"))
        {
            var fileIndex = 0;
            foreach (var file in request.Files.Where(f => f.FilePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
            {
                var path = Path.GetFullPath(file.FilePath);
                if (!File.Exists(path)) throw new FileNotFoundException("关联的 CSV 不存在", path);
                var imageId = HashId($"{id}/{fileIndex++}");
                var content = JingWangS3.ContentKey(options, imageId);
                await JingWangS3.UploadAsync(options, content + ".csv", path, cancellationToken);
                // 结果表和关系表共用 DATAGUID；文件表和关系表共用 IMGGUID。
                // 每次测试的 DATAGUID 与每个文件的 IMGGUID 分别标识，支持一笔测试关联多个文件。
                await ExecuteAsync(connection, transaction,
                    "INSERT INTO pc_img_data (IMGCONTENT,FILETYPE,IMGGUID,CREATETIME) VALUES (@path,'CSV',@guid,@time)",
                    cancellationToken, ("@path", content), ("@guid", imageId), ("@time", time));
                if (Enabled(options, "EnableCsvRelation"))
                    await ExecuteAsync(connection, transaction,
                        "INSERT INTO pc_img_rel_data (DATAGUID,IMGGUID,CREATETIME) VALUES (@data,@image,@time)",
                        cancellationToken, ("@data", id), ("@image", imageId), ("@time", time));
            }
        }
    }

    private static string? ValidateRecord(MesPluginOptions options, MesUploadRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Barcode)) return "检测记录缺少产品条码";
        if (request.EndTime == default) return "检测记录缺少结束时间";
        foreach (var key in new[] { "WorkOrder", "SpecCode", "Product", "Container", "PanelSequence" })
            if (string.IsNullOrWhiteSpace(options.Get(key))) return $"景旺配置缺少必填字段：{key}";
        if (Enabled(options, "EnableInspection") && request.TestItems.Count == 0) return "检测明细已启用，但没有测试项";
        if (Enabled(options, "EnableCsv"))
        {
            var error = JingWangS3.Validate(options);
            if (error is not null) return error;
            var files = request.Files.Where(file => file.FilePath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length == 0) return "CSV 记录已启用，但未生成单片 CSV";
            foreach (var file in files)
            {
                try
                {
                    var path = Path.GetFullPath(file.FilePath);
                    if (!File.Exists(path)) return "关联的单片 CSV 文件不存在";
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return "单片 CSV 文件路径无效";
                }
            }
        }
        if (Enabled(options, "EnableCsvRelation") && !Enabled(options, "EnableCsv")) return "CSV 关联启用时必须同时启用 CSV 记录";
        return null;
    }

    private static async Task<bool> RecordExistsAsync(MySqlConnection connection, MySqlTransaction transaction, string id, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand("SELECT EXISTS(SELECT 1 FROM pc_insph_data WHERE DATAGUID=@guid) OR EXISTS(SELECT 1 FROM pc_insp_data WHERE DATAGUID=@guid) OR EXISTS(SELECT 1 FROM pc_img_rel_data WHERE DATAGUID=@guid)", connection, transaction);
        cmd.Parameters.AddWithValue("@guid", id);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) != 0;
    }

    private static async Task<MySqlConnection> OpenAsync(MesPluginOptions options, CancellationToken ct)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = options.Get("DbHost", "127.0.0.1"), Port = uint.Parse(options.Get("DbPort", "3306"), CultureInfo.InvariantCulture),
            Database = options.Get("DbName", "pc_eap"), UserID = options.Get("DbUser", "root"), Password = options.Get("DbPassword"),
            ConnectionTimeout = 5, DefaultCommandTimeout = 10, Pooling = true, AllowUserVariables = false,
            SslMode = MySqlSslMode.None
        };
        var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private async Task<MesResult> QueueAndWriteEventAsync(MesPluginOptions options, string kind, string label,
        IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct, string? eventId = null)
    {
        var saved = false;
        await _writeGate.WaitAsync(ct);
        try
        {
            var store = new JingWangEventStore(options);
            if (eventId is not null && store.IsCompleted(eventId)) return MesResult.Success("该次工艺采集已写入，跳过重复提交", "DUPLICATE");
            if (eventId is not null && store.Load(eventId) is not null)
                return MesResult.Fail("该次工艺采集已在待处理列表，请核对后人工补写", "PENDING_DATABASE");
            var id = store.Save(kind, label, rows, eventId);
            saved = true;
            var record = store.Load(id)!;
            await ExecuteEventAsync(options, record, ct);
            store.Complete(record);
            return MesResult.Success($"{label}已写入");
        }
        catch (Exception ex)
        {
            var failure = SafeFailure($"{label}待写入", ex);
            return saved ? MesResult.Fail(failure.Message, "PENDING_DATABASE") : failure;
        }
        finally { _writeGate.Release(); }
    }

    private static async Task ExecuteEventAsync(MesPluginOptions options, EventRecord record, CancellationToken ct)
    {
        var sql = record.Kind switch
        {
            "EMP" => "INSERT INTO pc_data_emp (EMPNO,EMPNAME,EMPCODE,EMPDES,EMPTIME,CREATETIME) VALUES (@no,@name,@code,@description,@time,@time)",
            "SCAN" => "INSERT INTO pc_data_scan (CONTAINER,PNLIDORSETID,DRCTYPE,PARENTID,CREATEUSER,CREATETIME) VALUES (@lot,@barcode,@type,@parent,@user,@time)",
            "ALARM" => "INSERT INTO pc_data_alarm (ALARMCODE,ALARMDES,ALARMVALUE,CREATETIME) VALUES (@code,@description,@value,@time)",
            "CRAFT" => "INSERT INTO pc_data_craft (CONTAINER,PNLIDORSETID,ITEMCODE,ITEMDES,UOM,ITEMVALUE,ISOK,GROUPNUM,CREATETIME) VALUES (@lot,@panel,@item,@description,@unit,@value,@ok,@group,@time)",
            _ => throw new InvalidOperationException("未知景旺事件类型")
        };
        await using var connection = await OpenAsync(options, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var row in record.Rows)
        {
            if (record.Kind == "EMP" && row["description"] is "上机" or "下机") row["description"] += "时间";
            await ExecuteAsync(connection, transaction, sql, ct, row.Select(pair => ("@" + pair.Key, (object?)pair.Value)).ToArray());
        }
        await transaction.CommitAsync(ct);
    }

    private static Dictionary<string, string> Row(params (string Key, string Value)[] entries) =>
        entries.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

    private static async Task ExecuteAsync(MySqlConnection connection, MySqlTransaction? transaction, string sql,
        CancellationToken ct, params (string Name, object? Value)[] values)
    {
        await using var command = new MySqlCommand(sql, connection, transaction);
        foreach (var (name, value) in values) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<MesResult> ChangeToolAsync(MesPluginOptions options, string barcode, int? usedLife, CancellationToken ct = default)
    {
        if (!Enabled(options, "OnlineMode") || !Enabled(options, "EnableWebApi")) return MesResult.Fail("请先开启 MES");
        if (string.IsNullOrWhiteSpace(barcode)) return MesResult.Fail("请输入工治具条码");
        if (usedLife < 0) return MesResult.Fail("已使用寿命必须为非负整数");
        var operation = usedLife.HasValue ? "下机" : "上机";
        var url = usedLife.HasValue
            ? options.Get("ToolLogoutUrl", "http://10.51.8.4:8020/api/ToolLife/ResourceThruput")
            : options.Get("ToolLoginUrl", "http://10.51.8.4:8020/api/ToolLife/GetToolLifeQuery");
        object body = usedLife.HasValue
            ? new { BarCode = barcode.Trim(), UsedLifeQty = usedLife.Value.ToString(CultureInfo.InvariantCulture) }
            : new { BarCode = barcode.Trim() };
        try
        {
            using var document = await PostJsonAsync(url, body, options, ct);
            var root = document.RootElement;
            if (!bool.TryParse(Value(root, "Result"), out var ok) || !ok)
                return MesResult.Fail($"工治具{operation}失败：{Value(root, "Message")}", rawResponse: root.GetRawText());
            return MesResult.Success(usedLife.HasValue ? "工治具下机成功" :
                $"工治具上机成功，总寿命 {Value(root, "TotalLifeQty")}，已使用 {Value(root, "CurLifeQty")}", rawResponse: root.GetRawText());
        }
        catch (Exception ex) { return SafeFailure($"工治具{operation}失败", ex); }
    }

    private static readonly object ApiLogLock = new();

    private static async Task<JsonDocument> PostJsonAsync(string url, object body, MesPluginOptions options, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) throw new InvalidOperationException("景旺 API 地址未配置");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(int.TryParse(options.Get("ApiTimeoutSeconds"), out var seconds) ? seconds : 5, 1, 60)));
        var directory = options.Get("ApiLogDirectory", Path.Combine(AppContext.BaseDirectory, "data", "jingwang", "mes-log"));
        var now = DateTime.Now;
        var file = Path.Combine(directory, $"MES_LOG_{now:yyyy-MM-dd}.log");
        var id = Guid.NewGuid().ToString("N");
        void Log(string message)
        {
            lock (ApiLogLock)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{id}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }

        var json = JsonSerializer.Serialize(body, Json);
        Log($"POST {url}{Environment.NewLine}{json}");
        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(uri, content, timeout.Token);
            var raw = await response.Content.ReadAsStringAsync(timeout.Token);
            Log($"HTTP {(int)response.StatusCode}{Environment.NewLine}{raw}");
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(raw);
        }
        catch (Exception ex)
        {
            Log($"调用异常 {ex.GetType().Name}：{ex.Message}");
            throw;
        }
    }

    private static string Value(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) return property.Value.ToString();
        return string.Empty;
    }

    private static string FirstDecision(JsonElement root, string barcode)
    {
        if (root.ValueKind != JsonValueKind.Object) return string.Empty;
        foreach (var property in root.EnumerateObject())
            if (string.Equals(property.Name, "DataList", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Array)
                foreach (var item in property.Value.EnumerateArray())
                {
                    var code = Value(item, "pcsCode");
                    if (string.Equals(code, barcode, StringComparison.Ordinal)
                        || (string.IsNullOrWhiteSpace(code) && property.Value.GetArrayLength() == 1))
                        return Value(item, "result");
                }
        return string.Empty;
    }

    private static Dictionary<string, (string Code, string Description, string Unit)> ParseMappings(string text)
    {
        var result = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in text.Replace("\r\n", ";", StringComparison.Ordinal).Replace('\n', ';')
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0])) continue;
            var fields = pair[1].Split('|', StringSplitOptions.TrimEntries);
            if (fields.Length == 0 || string.IsNullOrWhiteSpace(fields[0])) continue;
            result[pair[0]] = (fields[0], fields.Length > 1 ? fields[1] : pair[0], fields.Length > 2 ? fields[2] : "");
        }
        return result;
    }

    private static string RecordId(MesUploadRequest request) => HashId($"{request.Barcode}\u001f{request.StartTime:O}\u001f{request.EndTime:O}");
    private static string HashId(string value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..32];
    private static bool Enabled(MesPluginOptions options, string key) => !string.Equals(options.Get(key, "true"), "false", StringComparison.OrdinalIgnoreCase);
    private static bool IsPass(string result) => result.Equals("PASS", StringComparison.OrdinalIgnoreCase) || result.Equals("OK", StringComparison.OrdinalIgnoreCase);
    private static string First(string first, string second) => string.IsNullOrWhiteSpace(first) ? second : first;
    private static MesPluginConfigField Field(string key, string label, string defaultValue = "", bool required = false) =>
        new() { Key = key, DisplayName = label, DefaultValue = defaultValue, IsRequired = required };
    private static MesResult SafeFailure(string label, Exception ex)
    {
        // 初始化异常的外层类型没有定位价值；只提取安全的组件名，不输出连接字符串或密码。
        while ((ex is TypeInitializationException or System.Reflection.TargetInvocationException) && ex.InnerException is not null)
            ex = ex.InnerException;
        var assemblyName = ex switch
        {
            FileNotFoundException missing => missing.FileName,
            FileLoadException failed => failed.FileName,
            _ => null
        };
        if (!string.IsNullOrWhiteSpace(assemblyName)
            && (assemblyName.Contains("Version=", StringComparison.OrdinalIgnoreCase)
                || assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            return MesResult.Fail($"{label}：运行组件 {Path.GetFileName(assemblyName.Split(',')[0])} 缺失或无法加载，请更新完整程序目录并重启上位机");
        return ex switch
        {
            MySqlException mysql => MesResult.Fail($"{label}：MySQL 错误 {mysql.Number}，请检查配置和数据库日志"),
            HttpRequestException http => MesResult.Fail($"{label}：HTTP 状态 {(int?)http.StatusCode ?? 0}"),
            OperationCanceledException => MesResult.Fail($"{label}：请求超时或取消"),
            _ => MesResult.Fail($"{label}：{ex.GetType().Name}，请核对插件依赖和配置")
        };
    }
}
