using System.Windows.Input;
using System.Collections.ObjectModel;
using CH_V03.App.Commands;
using CH_V03.Core.Configuration;
using CH_V03.Core.Mes;
using CH_V03.Core.ResultExport;

namespace CH_V03.App.ViewModels;

public sealed class PluginConfigurationViewModel : DialogViewModelBase
{
    private static readonly string[] DefaultExportPluginIds =
    {
        "Default.Csv",
        "Default.Dcl",
        "Default.CsvMes"
    };

    private static readonly HashSet<string> HongXinOaFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Endpoint", "UploadType", "MaterialNo", "DeviceNo", "ItemId", "BatchNo", "TimeoutSeconds"
    };

    private static readonly HashSet<string> HongXinCommonMesFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "MesUploadMode", "MesBasicAuthUser", "MesBasicAuthPassword", "MesTerminalMac", "MesTerminalName",
        "MesOperatorId", "MesStartScStatus", "MesStartWi09Ruid", "MesStartScStno",
        "MesStartPtNo", "MesStartVcNo"
    };

    private static readonly HashSet<string> HongXinMappingFieldKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "MesCharacteristicCodeMap", "MesDefectCodeMap"
    };

    private readonly IReadOnlyDictionary<string, IMesPlugin> _mesPlugins;
    private readonly IReadOnlyDictionary<string, IResultExportPlugin> _exportPlugins;
    private string _jingWangS3SecretKey = string.Empty;
    private string _jingWangDbPassword = string.Empty;
    private string _jingWangConnectionMessage = "未测试";

    public PluginConfigurationViewModel(
        AppConfiguration configuration,
        IEnumerable<IMesPlugin> mesPlugins,
        IEnumerable<IResultExportPlugin> exportPlugins, bool jingWangAccount = false)
    {
        Configuration = configuration;
        ConfirmJingWangResults = jingWangAccount;
        _mesPlugins = mesPlugins.ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        _exportPlugins = exportPlugins.ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase);
        MesPluginChoices = BuildMesPluginChoices(_mesPlugins.Values).ToList();
        ExportPluginChoices = BuildExportPluginChoices(_exportPlugins.Values).ToList();
        MesPluginFields = BuildMesPluginFields();
        UpdateHongXinMesFieldGroups();
        UpdateJingWangFields();
        ExportPluginFields = BuildExportPluginFields();
        SaveCommand = new RelayCommand(() => Close(true));
        CancelCommand = new RelayCommand(() => Close(false));
        TestJingWangDatabaseCommand = new AsyncRelayCommand(TestJingWangDatabaseAsync);
        RefreshJingWangPendingCommand = new RelayCommand(RefreshJingWangPending);
        RetryJingWangDatabaseCommand = new AsyncRelayCommand(RetryJingWangDatabaseAsync);
        RetryJingWangApisCommand = new AsyncRelayCommand(RetryJingWangApisAsync);
        ArchiveJingWangPendingCommand = new RelayCommand(ArchiveJingWangPending);
        ToolLoginCommand = new AsyncRelayCommand(() => ChangeToolAsync(false));
        ToolLogoutCommand = new AsyncRelayCommand(() => ChangeToolAsync(true));
        ToolBarcode = configuration.User.FixtureNo;
        RefreshJingWangPending();
    }

    public AppConfiguration Configuration { get; }

    public IReadOnlyList<PluginSelectionItemViewModel> MesPluginChoices { get; }

    public IReadOnlyList<PluginSelectionItemViewModel> ExportPluginChoices { get; }

    public IReadOnlyList<PluginConfigFieldViewModel> MesPluginFields { get; private set; }

    public IReadOnlyList<PluginConfigFieldViewModel> OaMesPluginFields { get; private set; } = Array.Empty<PluginConfigFieldViewModel>();

    public IReadOnlyList<PluginConfigFieldViewModel> CommonMesPluginFields { get; private set; } = Array.Empty<PluginConfigFieldViewModel>();

    public IReadOnlyList<PluginConfigFieldViewModel> CapacityMesPluginFields { get; private set; } = Array.Empty<PluginConfigFieldViewModel>();

    public IReadOnlyList<PluginConfigFieldViewModel> StationMesPluginFields { get; private set; } = Array.Empty<PluginConfigFieldViewModel>();

    public IReadOnlyList<PluginConfigFieldViewModel> MappingMesPluginFields { get; private set; } = Array.Empty<PluginConfigFieldViewModel>();

    public IReadOnlyList<PluginConfigFieldViewModel> ExportPluginFields { get; private set; }

    public bool HasMesPluginFields => MesPluginFields.Count > 0;

    public bool IsHongXinMesPlugin => string.Equals(SelectedMesPluginId, "HongXin.OA", StringComparison.OrdinalIgnoreCase);

    public bool IsJingWangMesPlugin => string.Equals(SelectedMesPluginId, "JingWang.Eap", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<PluginConfigFieldViewModel> JingWangDbFields { get; private set; } = [];
    public IReadOnlyList<PluginConfigFieldViewModel> JingWangCraftFields { get; private set; } = [];
    public IReadOnlyList<PluginConfigFieldViewModel> JingWangScanFields { get; private set; } = [];
    public IReadOnlyList<PluginConfigFieldViewModel> JingWangAlarmFields { get; private set; } = [];
    public IReadOnlyList<PluginConfigFieldViewModel> JingWangEmployeeFields { get; private set; } = [];
    public IReadOnlyList<PluginConfigFieldViewModel> JingWangInspectionFields { get; private set; } = [];
    public IReadOnlyList<PluginConfigFieldViewModel> JingWangResultFields { get; private set; } = [];
    public IReadOnlyList<PluginConfigFieldViewModel> JingWangCsvFields { get; private set; } = [];
    public IReadOnlyList<PluginConfigFieldViewModel> JingWangApiFields { get; private set; } = [];

    public string JingWangDbPassword
    {
        get => _jingWangDbPassword;
        set
        {
            if (!SetField(ref _jingWangDbPassword, value ?? string.Empty)) return;
            if (string.IsNullOrEmpty(value)) Configuration.Mes.PluginOptions.Remove("DbPasswordProtected");
            else Configuration.Mes.PluginOptions["DbPasswordProtected"] = MesSecretProtector.Protect(value);
        }
    }

    public string JingWangS3SecretKey
    {
        get => _jingWangS3SecretKey;
        set
        {
            if (!SetField(ref _jingWangS3SecretKey, value ?? string.Empty)) return;
            if (string.IsNullOrEmpty(value)) Configuration.Mes.PluginOptions.Remove("S3SecretKeyProtected");
            else Configuration.Mes.PluginOptions["S3SecretKeyProtected"] = MesSecretProtector.Protect(value);
        }
    }

    public string JingWangConnectionMessage
    {
        get => _jingWangConnectionMessage;
        private set => SetField(ref _jingWangConnectionMessage, value);
    }

    public ICommand TestJingWangDatabaseCommand { get; private set; } = null!;
    public ICommand RefreshJingWangPendingCommand { get; private set; } = null!;
    public ICommand RetryJingWangDatabaseCommand { get; private set; } = null!;
    public ICommand RetryJingWangApisCommand { get; private set; } = null!;
    public ICommand ArchiveJingWangPendingCommand { get; private set; } = null!;
    public ObservableCollection<JingWangPendingRecord> JingWangPendingRecords { get; } = new();
    public JingWangPendingRecord? SelectedJingWangPendingRecord { get; set; }

    public bool HasOtherMesPluginFields => HasMesPluginFields && !IsHongXinMesPlugin && !IsJingWangMesPlugin;

    public bool HasExportPluginFields => ExportPluginFields.Count > 0;

    public bool IsDefaultMesSelected => string.IsNullOrWhiteSpace(SelectedMesPluginId);

    public bool IsJiaLianYiCareerRuleMesPlugin => string.Equals(
        SelectedMesPluginId,
        "JiaLianYi.CareerRule",
        StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 嘉联益 CareerRule 复用默认 MES 的基础资料（站位、账号、工单等），
    /// 这些资料必须显示在该插件专有字段之前。
    /// </summary>
    public bool ShouldShowDefaultMesSettings => IsDefaultMesSelected || IsJiaLianYiCareerRuleMesPlugin;

    public bool IsDefaultExportSelected => string.IsNullOrWhiteSpace(SelectedExportPluginId);

    public string SelectedMesPluginId
    {
        get => string.Equals(Configuration.Mes.SelectedPlugin, "None", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : Configuration.Mes.SelectedPlugin;
        set
        {
            Configuration.Mes.SelectedPlugin = string.IsNullOrWhiteSpace(value) ? "None" : value;
            Configuration.Mes.Enabled = !string.IsNullOrWhiteSpace(value);
            if (IsJingWangMesPlugin)
            {
                Configuration.Output.SinglePieceCsvEnabled = true;
                if (!Configuration.Export.EnabledPluginIds.Contains("Default.Csv", StringComparer.OrdinalIgnoreCase))
                    Configuration.Export.EnabledPluginIds.Add("Default.Csv");
            }
            MesPluginFields = BuildMesPluginFields();
            UpdateHongXinMesFieldGroups();
            UpdateJingWangFields();
            RefreshJingWangPending();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedMesPluginName));
            OnPropertyChanged(nameof(MesPluginFields));
            OnPropertyChanged(nameof(HasMesPluginFields));
            OnPropertyChanged(nameof(IsHongXinMesPlugin));
            OnPropertyChanged(nameof(IsJingWangMesPlugin));
            OnPropertyChanged(nameof(HasOtherMesPluginFields));
            OnPropertyChanged(nameof(IsDefaultMesSelected));
            OnPropertyChanged(nameof(IsJiaLianYiCareerRuleMesPlugin));
            OnPropertyChanged(nameof(ShouldShowDefaultMesSettings));
        }
    }

    public string SelectedMesPluginName => MesPluginChoices
        .FirstOrDefault(item => string.Equals(item.Id, SelectedMesPluginId, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? "不加载 MES 插件";

    public string SelectedExportPluginId
    {
        get
        {
            var enabled = Configuration.Export.EnabledPluginIds;
            if (enabled.Count == 0 || IsDefaultExport(enabled))
            {
                return string.Empty;
            }

            return enabled.FirstOrDefault() ?? string.Empty;
        }
        set
        {
            Configuration.Export.EnabledPluginIds.Clear();
            if (string.IsNullOrWhiteSpace(value))
            {
                Configuration.Export.EnabledPluginIds.AddRange(DefaultExportPluginIds);
            }
            else
            {
                Configuration.Export.EnabledPluginIds.Add(value);
            }

            ExportPluginFields = BuildExportPluginFields();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedExportPluginName));
            OnPropertyChanged(nameof(ExportPluginFields));
            OnPropertyChanged(nameof(HasExportPluginFields));
            OnPropertyChanged(nameof(IsDefaultExportSelected));
        }
    }

    public string SelectedExportPluginName => ExportPluginChoices
        .FirstOrDefault(item => string.Equals(item.Id, SelectedExportPluginId, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? "默认导出器";

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    private IReadOnlyList<PluginConfigFieldViewModel> BuildMesPluginFields()
    {
        if (string.IsNullOrWhiteSpace(SelectedMesPluginId)
            || !_mesPlugins.TryGetValue(SelectedMesPluginId, out var plugin))
        {
            return Array.Empty<PluginConfigFieldViewModel>();
        }

        return plugin.GetConfigFields()
            .Where(field => Configuration.Compatibility.IsFieldVisible($"MesPlugin.{field.Key}"))
            .Select(field => new PluginConfigFieldViewModel(
                field.Key,
                field.DisplayName,
                field.DefaultValue,
                field.IsRequired,
                field.IsSecret,
                IsJingWangMesPlugin && field.Key.StartsWith("Enable", StringComparison.OrdinalIgnoreCase),
                string.Empty,
                Configuration.Mes.PluginOptions))
            .ToList();
    }

    private IReadOnlyList<PluginConfigFieldViewModel> BuildExportPluginFields()
    {
        var pluginId = string.IsNullOrWhiteSpace(SelectedExportPluginId) ? "Default.Csv" : SelectedExportPluginId;
        if (!_exportPlugins.TryGetValue(pluginId, out var plugin))
        {
            return Array.Empty<PluginConfigFieldViewModel>();
        }

        if (!Configuration.Export.PluginOptions.TryGetValue(pluginId, out var options))
        {
            options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Configuration.Export.PluginOptions[pluginId] = options;
        }

        return plugin.GetConfigFields()
            .Where(field => field.Key == SpecialTestValue.OptionKey
                || Configuration.Compatibility.IsFieldVisible($"ExportPlugin.{field.Key}"))
            .Select(field => new PluginConfigFieldViewModel(
                field.Key,
                field.DisplayName,
                field.DefaultValue,
                field.IsRequired,
                field.IsSecret,
                field.IsCheckbox,
                field.HelpText,
                options))
            .ToList();
    }

    private void UpdateHongXinMesFieldGroups()
    {
        if (!IsHongXinMesPlugin)
        {
            OaMesPluginFields = Array.Empty<PluginConfigFieldViewModel>();
            CommonMesPluginFields = Array.Empty<PluginConfigFieldViewModel>();
            CapacityMesPluginFields = Array.Empty<PluginConfigFieldViewModel>();
            StationMesPluginFields = Array.Empty<PluginConfigFieldViewModel>();
            MappingMesPluginFields = Array.Empty<PluginConfigFieldViewModel>();
        }
        else
        {
            OaMesPluginFields = MesPluginFields.Where(field => HongXinOaFieldKeys.Contains(field.Key)).ToList();
            CommonMesPluginFields = MesPluginFields.Where(field => HongXinCommonMesFieldKeys.Contains(field.Key)).ToList();
            CapacityMesPluginFields = MesPluginFields
                .Where(field => field.Key.StartsWith("MesCapacity", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(field.Key, "MesCapacityScRcno", StringComparison.OrdinalIgnoreCase))
                .ToList();
            StationMesPluginFields = MesPluginFields
                .Where(field => field.Key.StartsWith("MesStation", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(field.Key, "MesStationScRcno", StringComparison.OrdinalIgnoreCase))
                .ToList();
            MappingMesPluginFields = MesPluginFields.Where(field => HongXinMappingFieldKeys.Contains(field.Key)).ToList();
        }

        OnPropertyChanged(nameof(OaMesPluginFields));
        OnPropertyChanged(nameof(CommonMesPluginFields));
        OnPropertyChanged(nameof(CapacityMesPluginFields));
        OnPropertyChanged(nameof(StationMesPluginFields));
        OnPropertyChanged(nameof(MappingMesPluginFields));
    }

    private void UpdateJingWangFields()
    {
        IReadOnlyList<PluginConfigFieldViewModel> Pick(params string[] keys) =>
            IsJingWangMesPlugin
                ? MesPluginFields.Where(field => keys.Contains(field.Key, StringComparer.OrdinalIgnoreCase) && !(ConfirmJingWangResults && field.Key is ("EnableWebApi" or "EnableInspection" or "EnableInspectionResult" or "EnableEmployee"))).ToList()
                : Array.Empty<PluginConfigFieldViewModel>();

        JingWangDbFields = Pick("DbHost", "DbPort", "DbName", "DbUser");
        JingWangCraftFields = Pick("EnableCraft", "CraftMinutes", "CraftMappings");
        JingWangScanFields = Pick("EnableScan", "ScanType");
        JingWangAlarmFields = Pick("EnableAlarm");
        JingWangEmployeeFields = Pick("EnableEmployee", "EquipmentCode", "EmployeeCardCode");
        JingWangInspectionFields = Pick("EnableInspection", "Product", "Container", "SetId");
        JingWangResultFields = Pick("EnableInspectionResult", "WorkOrder", "SpecCode", "PanelSequence");
        JingWangCsvFields = Pick("EnableCsv", "EnableCsvRelation", "S3Endpoint", "S3Bucket", "S3Prefix", "S3Region", "S3AccessKey");
        JingWangApiFields = Pick("EnableWebApi", "LoginUrl", "StationUrl", "RecheckUrl", "ApiTimeoutSeconds");

        _jingWangDbPassword = string.Empty;
        if (IsJingWangMesPlugin && Configuration.Mes.PluginOptions.TryGetValue("DbPasswordProtected", out var secret))
        {
            try { _jingWangDbPassword = MesSecretProtector.Unprotect(secret); }
            catch (Exception) { JingWangConnectionMessage = "保存的密码无法解密，请重新输入"; }
        }
        OnPropertyChanged(nameof(JingWangDbPassword));
        _jingWangS3SecretKey = string.Empty;
        if (IsJingWangMesPlugin && Configuration.Mes.PluginOptions.TryGetValue("S3SecretKeyProtected", out var s3Secret))
        {
            try { _jingWangS3SecretKey = MesSecretProtector.Unprotect(s3Secret); }
            catch (Exception) { JingWangConnectionMessage = "保存的密码无法解密，请重新输入"; }
        }
        OnPropertyChanged(nameof(JingWangS3SecretKey));
        foreach (var name in new[] { nameof(JingWangDbFields), nameof(JingWangCraftFields), nameof(JingWangScanFields),
                     nameof(JingWangAlarmFields), nameof(JingWangEmployeeFields), nameof(JingWangInspectionFields),
                     nameof(JingWangLogFields), nameof(JingWangToolFields), nameof(JingWangResultFields), nameof(JingWangCsvFields), nameof(JingWangApiFields) })
            OnPropertyChanged(name);
    }

    private async Task TestJingWangDatabaseAsync()
    {
        if (!_mesPlugins.TryGetValue("JingWang.Eap", out var plugin)) return;
        try
        {
            var values = new Dictionary<string, string>(Configuration.Mes.PluginOptions, StringComparer.OrdinalIgnoreCase)
            {
                ["DbPassword"] = JingWangDbPassword
            };
            JingWangConnectionMessage = (await plugin.TestConnectionAsync(new MesPluginOptions { Values = values })).Message;
        }
        catch (Exception ex) { JingWangConnectionMessage = $"连接检查失败：{ex.GetType().Name}"; }
    }

    private MesPluginOptions BuildJingWangOptions()
    {
        var values = new Dictionary<string, string>(Configuration.Mes.PluginOptions, StringComparer.OrdinalIgnoreCase)
        {
            ["S3SecretKey"] = JingWangS3SecretKey,
            ["DbPassword"] = JingWangDbPassword,
            ["OnlineMode"] = Configuration.Mes.OnlineMode ? "true" : "false"
        };
        return new MesPluginOptions { Values = values };
    }

    private void RefreshJingWangPending()
    {
        JingWangPendingRecords.Clear();
        if (!IsJingWangMesPlugin || !_mesPlugins.TryGetValue("JingWang.Eap", out var selected) || selected is not IJingWangMesPlugin plugin) return;
        try
        {
            foreach (var item in plugin.GetPendingRecords(BuildJingWangOptions())) JingWangPendingRecords.Add(item);
        }
        catch (Exception ex) { JingWangConnectionMessage = $"读取待处理记录失败：{ex.GetType().Name}"; }
    }

    private async Task RetryJingWangDatabaseAsync()
    {
        if (ConfirmJingWangResults && !Configuration.Mes.OnlineMode)
        {
            JingWangConnectionMessage = "请先在主界面开启 MES";
            return;
        }
        if (SelectedJingWangPendingRecord is null || !_mesPlugins.TryGetValue("JingWang.Eap", out var selected) || selected is not IJingWangMesPlugin plugin) return;
        JingWangConnectionMessage = (await plugin.RetryPendingDatabaseAsync(BuildJingWangOptions(), SelectedJingWangPendingRecord.Id)).Message;
        RefreshJingWangPending();
    }

    public bool ConfirmJingWangResults { get; }
    public string ToolBarcode { get; set; }
    public string ToolUsedLife { get; set; } = "";
    public ICommand ToolLoginCommand { get; }
    public ICommand ToolLogoutCommand { get; }
    private bool _toolBusy;
    private async Task ChangeToolAsync(bool unload)
    {
        if (_toolBusy || !_mesPlugins.TryGetValue("JingWang.Eap", out var selected) || selected is not IJingWangMesPlugin plugin) return;
        int? used = null;
        if (unload)
        {
            if (!int.TryParse(ToolUsedLife, out var count) || count < 0)
            {
                JingWangConnectionMessage = "请填写本次下机上报的已使用寿命（非负整数）";
                return;
            }
            used = count;
        }
        _toolBusy = true;
        try { JingWangConnectionMessage = (await plugin.ChangeToolAsync(BuildJingWangOptions(), ToolBarcode, used)).Message; }
        finally { _toolBusy = false; }
    }
    public IReadOnlyList<PluginConfigFieldViewModel> JingWangLogFields => MesPluginFields.Where(f => f.Key == "ApiLogDirectory").ToList();
    public IReadOnlyList<PluginConfigFieldViewModel> JingWangToolFields => MesPluginFields.Where(f => f.Key is "ToolLoginUrl" or "ToolLogoutUrl").ToList();

    private async Task RetryJingWangApisAsync()
    {
        if (SelectedJingWangPendingRecord is null || !_mesPlugins.TryGetValue("JingWang.Eap", out var selected) || selected is not IJingWangMesPlugin plugin) return;
        var record = SelectedJingWangPendingRecord;
        var result = await plugin.RetryPendingApisAsync(BuildJingWangOptions(), record.Id);
        JingWangConnectionMessage = result.Message;
        if (ConfirmJingWangResults && (!result.IsSuccess || result.Code != "JINGWANG_OK"))
            System.Windows.MessageBox.Show($"SN {record.Barcode}\n{result.Message}", "景旺 MES 复判提醒", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        RefreshJingWangPending();
    }

    private void ArchiveJingWangPending()
    {
        if (SelectedJingWangPendingRecord is null || !_mesPlugins.TryGetValue("JingWang.Eap", out var selected) || selected is not IJingWangMesPlugin plugin) return;
        var question = CH_V03.App.Localization.UiLanguage.IsEnglish
            ? $"Mark {SelectedJingWangPendingRecord.Barcode} as manually reviewed without retrying upload? The original record will remain on this computer."
            : $"确认将 {SelectedJingWangPendingRecord.Barcode} 标记为已人工核对且不补传？原始记录会保留在本机。";
        var answer = System.Windows.MessageBox.Show(question,
            CH_V03.App.Localization.UiLanguage.Text("景旺 MES 待处理记录"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;
        JingWangConnectionMessage = plugin.ArchivePendingRecord(BuildJingWangOptions(), SelectedJingWangPendingRecord.Id).Message;
        RefreshJingWangPending();
    }

    private static IEnumerable<PluginSelectionItemViewModel> BuildMesPluginChoices(IEnumerable<IMesPlugin> mesPlugins)
    {
        yield return new PluginSelectionItemViewModel
        {
            Id = string.Empty,
            DisplayName = "默认 MES（不加载插件）"
        };

        foreach (var plugin in mesPlugins)
        {
            yield return new PluginSelectionItemViewModel
            {
                Id = plugin.Id,
                DisplayName = plugin.Name
            };
        }
    }

    private static IEnumerable<PluginSelectionItemViewModel> BuildExportPluginChoices(IEnumerable<IResultExportPlugin> exportPlugins)
    {
        yield return new PluginSelectionItemViewModel
        {
            Id = string.Empty,
            DisplayName = "默认导出器（CSV + DCL + CsvMes）"
        };

        foreach (var plugin in exportPlugins.Where(plugin => !plugin.Id.StartsWith("Default.", StringComparison.OrdinalIgnoreCase)))
        {
            yield return new PluginSelectionItemViewModel
            {
                Id = plugin.Id,
                DisplayName = plugin.Name
            };
        }
    }

    private static bool IsDefaultExport(IReadOnlyCollection<string> enabledPluginIds)
    {
        return enabledPluginIds.Count == DefaultExportPluginIds.Length
            && DefaultExportPluginIds.All(defaultId => enabledPluginIds.Contains(defaultId, StringComparer.OrdinalIgnoreCase));
    }
}
