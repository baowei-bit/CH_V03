using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.App.Communication;
using CH_V03.App.Services;
using CH_V03.Core.Communication;
using CH_V03.Core.Configuration;
using CH_V03.Core.Mes;
using CH_V03.Core.ResultExport;
using CH_V03.Core.TestFlow;

namespace CH_V03.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private enum ScanAheadStatus
    {
        None,
        Ok,
        Ng
    }

    private const string SampleItemsPayload = "Item1,J1_1-SHORT,10,0,ohm,1,1;Item2,R1,11.000,9.000,Kohm,1,1;Item3,U1-ID,0x38,0x38,HEX,1,1;3;end";
    private const string SamplePassPayload = "Test1,5.000,OK;Test2,9.983,OK;Test3,0x38,OK;3;end";
    private const string SampleFailPayload = "Test1,5.000,OK;Test2,12.600,NG;Test3,0x38,OK;3;end";
    private const int ScannerReceiveDebounceMilliseconds = 120;
    private const int TestItemRequestMaxAttempts = 3;
    private const int JiaLianYiMesUploadTimeoutSeconds = 8;
    private static readonly System.Text.RegularExpressions.Regex LowerMachineFrameStartPattern = new(
        @"(?<![A-Za-z0-9])(?:Item1|Test1)\s*,",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex LowerMachineItemFrameStartPattern = new(
        @"(?<![A-Za-z0-9])Item1\s*,",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex LowerMachineResetSignalPattern = new(
        @"(?<![A-Za-z0-9])reset(?![A-Za-z0-9])",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly ConfigurationStore _configurationStore;
    private readonly CommunicationTestService _communicationTestService;
    private readonly DeviceConnectionFactory _deviceConnectionFactory;
    private readonly IConfigurationDialogService _configurationDialogService;
    private readonly ITestProtocolAdapter _protocolAdapter;
    private readonly TestFlowService _testFlowService;
    private readonly ExportRuntimeService _exportRuntimeService;
    private readonly MesRuntimeService _mesRuntimeService;
    private readonly AuthenticationService _authenticationService;
    private readonly IReadOnlyList<ProjectProfile> _projectProfiles;
    private readonly SynchronizationContext _uiContext;
    private readonly RuntimeLogWriter _runtimeLogWriter = new();
    private AppConfiguration _configuration;
    private IDeviceConnection? _scannerConnection;
    private IDeviceConnection? _lowerMachineConnection;
    private string _statusMessage = string.Empty;
    private string _scannerState = "未连接";
    private string _lowerMachineState = "未连接";
    private string _mesState = "未启用";
    private string _testState = "WAIT";
    private string _currentBarcode = string.Empty;
    private string _lastTestStartedBarcode = string.Empty;
    private string _lastFailItem = string.Empty;
    private string _popupMessage = string.Empty;
    private string _testTime = "0.00S";
    private string _lastTestResult = "-";
    private bool _isLogVisible = true;
    private bool _isAdminToolsVisible;
    private bool _isPopupVisible;
    private int _passCount;
    private int _failCount;
    private int _currentConsecutiveNg;
    private string _scannerReceiveBuffer = string.Empty;
    private string _lowerMachineReceiveBuffer = string.Empty;
    private bool _lowerMachineProtocolConfirmed;
    private bool _lowerMachineItemRequestSent;
    private TaskCompletionSource<string>? _lowerMachineHandshakeReply;
    private TaskCompletionSource<bool>? _lowerMachineItemHandshakeReply;
    private LowerMachineHandshakeReplyParser? _lowerMachineHandshakeParser;
    private bool _waitingForItemHeader;
    private bool _itemHeaderReceptionStarted;
    private bool _startTestAfterItemHeader;
    private bool _discardLowerMachineDataUntilNextStartSignal;
    private ScanAheadStatus _scanAheadStatus;
    private CancellationTokenSource? _scannerReceiveDebounceCts;
    private CancellationTokenSource? _scannerWaitTimeoutCts;
    private CancellationTokenSource? _lowerMachinePayloadTimeoutCts;
    private CancellationTokenSource? _testItemRequestRetryCts;
    private bool _ignoreScannerDataUntilNextStartSignal;
    private readonly Queue<PopupNotification> _popupQueue = new();
    private PopupNotification? _activePopup;
    private TestFlowSnapshot? _pendingJlyRetestFailure;
    private DateTime? _lowerMachineStartSignalTime;
    private readonly Timer _jiaLianYiModeTimer;
    private readonly Timer _jingWangCraftTimer;
    private TestFlowSnapshot? _jingWangLatestCraftSnapshot;
    private readonly JingWangCraftSchedule _jingWangCraftSchedule = new(DateTime.Now);
    private string _jingWangPrecheckStatus = "未过站";
    private string _jingWangPrecheckBarcode = string.Empty;
    private readonly HashSet<string> _jingWangActiveAlarms = new(StringComparer.Ordinal);
    private string _lastJiaLianYiAutoStandardSwitch = string.Empty;
    private readonly SemaphoreSlim _jiaLianYiMesUploadGate = new(1, 1);
    private readonly object _jiaLianYiMesUploadSync = new();
    private int _pendingJiaLianYiMesUploadCount;
    private TaskCompletionSource? _jiaLianYiMesUploadCompletion;

    public MainWindowViewModel(
        ConfigurationStore configurationStore,
        CommunicationTestService communicationTestService,
        DeviceConnectionFactory deviceConnectionFactory,
        IConfigurationDialogService configurationDialogService,
        ITestProtocolAdapter protocolAdapter,
        TestFlowService testFlowService,
        ExportRuntimeService exportRuntimeService,
        MesRuntimeService mesRuntimeService,
        AuthenticationService authenticationService,
        IReadOnlyList<ProjectProfile> projectProfiles)
    {
        _configurationStore = configurationStore;
        _communicationTestService = communicationTestService;
        _deviceConnectionFactory = deviceConnectionFactory;
        _configurationDialogService = configurationDialogService;
        _protocolAdapter = protocolAdapter;
        _testFlowService = testFlowService;
        _exportRuntimeService = exportRuntimeService;
        _mesRuntimeService = mesRuntimeService;
        _authenticationService = authenticationService;
        _projectProfiles = projectProfiles;
        _uiContext = SynchronizationContext.Current ?? new SynchronizationContext();
        _configuration = _configurationStore.Load();
        var accountModeChanged = _authenticationService.ApplyAccountMode(_configuration);
        if (IsJingWangAccount) ConfigureJingWangSession();
        if (!IsJingWangAccount && MesRuntimeService.IsJingWang(_configuration) && _configuration.Mes.OnlineMode)
        {
            _configuration.Mes.OnlineMode = false;
            _configurationStore.Save(_configuration);
        }
        var appliedJiaLianYiDefaults = ApplyKunshanJiaLianYiDedicatedDefaults(_configuration);
        var startedInJiaLianYiProductionMode = ApplyJiaLianYiStartupProductionMode(_configuration);
        NormalizeScanMode(_configuration.User);
        if (accountModeChanged || appliedJiaLianYiDefaults || startedInJiaLianYiProductionMode)
        {
            _configurationStore.Save(_configuration);
        }

        OpenBasicConfigurationCommand = new RelayCommand(OpenBasicConfiguration);
        OpenCommunicationConfigurationCommand = new AsyncRelayCommand(OpenCommunicationConfigurationAsync);
        OpenEndoscopeCommand = new AsyncRelayCommand(OpenEndoscopeAsync);
        OpenMesConfigurationCommand = new RelayCommand(OpenMesConfiguration);
        OpenMesDebugToolCommand = new RelayCommand(OpenMesDebugTool);
        OpenPluginConfigurationCommand = new RelayCommand(OpenPluginConfiguration);
        OpenKunshanJiaLianYiConfigurationCommand = new RelayCommand(OpenKunshanJiaLianYiConfiguration);
        OpenTestItemConfigurationCommand = new RelayCommand(OpenTestItemConfiguration);
        ImportProjectProfileCommand = new RelayCommand(ImportProjectProfile);
        OpenUserConfigurationCommand = new RelayCommand(OpenUserConfiguration);
        SaveConfigurationCommand = new RelayCommand(Save);
        ConnectDevicesCommand = new AsyncRelayCommand(ConnectDevicesAsync);
        ReadCompanionIdCommand = new AsyncRelayCommand(ReadCompanionIdAsync);
        BurnCompanionCommand = new AsyncRelayCommand(() => MaintainCompanionAsync(true));
        WriteCompanionCommand = new AsyncRelayCommand(() => MaintainCompanionAsync(false));
        ReleaseFixtureCommand = new AsyncRelayCommand(ReleaseFixtureAsync);
        DisconnectCompanionCommand = new RelayCommand(DisconnectCompanion);
        CheckCompanionCommand = new AsyncRelayCommand(CheckCompanionAsync);

        DisconnectDevicesCommand = new AsyncRelayCommand(DisconnectDevicesAsync);
        ToggleScannerConnectionCommand = new AsyncRelayCommand(ToggleScannerConnectionAsync);
        ToggleLowerMachineConnectionCommand = new AsyncRelayCommand(ToggleLowerMachineConnectionAsync);
        TestScannerCommand = new AsyncRelayCommand(TestScannerAsync);
        TestLowerMachineCommand = new AsyncRelayCommand(TestLowerMachineAsync);
        ToggleOnlineModeCommand = new AsyncRelayCommand(ToggleOnlineModeAsync);
        SelectStandardPieceModeCommand = new RelayCommand(() => SelectJiaLianYiRunMode(JiaLianYiRunMode.StandardPiece));
        SelectTestPieceModeCommand = new RelayCommand(() => SelectJiaLianYiRunMode(JiaLianYiRunMode.TestPiece));
        SelectDebugModeCommand = new RelayCommand(() => SelectJiaLianYiRunMode(JiaLianYiRunMode.Debug));
        ToggleLogVisibilityCommand = new RelayCommand(ToggleLogVisibility);
        ToggleAdminToolsCommand = new RelayCommand(ToggleAdminTools);
        ClearYieldCommand = new RelayCommand(ClearYield);
        OpenDataAnalysisCommand = new RelayCommand(OpenDataAnalysis);
        OpenTestTrendAnalysisCommand = new RelayCommand(OpenTestTrendAnalysis);
        InitializeFlowCommand = new RelayCommand(InitializeFlow);
        SimulateLowerMachineStartCommand = new RelayCommand(SimulateLowerMachineStart);
        SimulateScanCommand = new RelayCommand(SimulateScan);
        SimulatePassCommand = new AsyncRelayCommand(() => CompleteByPayloadAsync(SamplePassPayload, true));
        SimulateFailCommand = new AsyncRelayCommand(() => CompleteByPayloadAsync(SampleFailPayload, true));
        SimulatePopupCommand = new AsyncRelayCommand(SimulatePopupAsync);
        SwitchLoginCommand = new RelayCommand(SwitchLogin);
        ConfirmPopupCommand = new RelayCommand(ConfirmPopup);
        HelpCommand = new RelayCommand(() => _configurationDialogService.ShowHelp(_configuration));

        TestItems = new ObservableCollection<TestItemViewModel>();
        Logs = new ObservableCollection<string>();
        ProductionRecords = new ObservableCollection<ProductionRecordViewModel>();
        RefreshDerivedState();
        RestoreDailyYieldStatistics();
        ApplySnapshot(_testFlowService.LoadConfiguredTestItems(_configuration.TestItems.Items), countYield: false);
        AddLog("系统启动，配置已加载");
        if (startedInJiaLianYiProductionMode)
        {
            AddLog("昆山嘉联益启动后已自动进入生产模式");
        }
        AddLog($"协议适配器已加载：{_protocolAdapter.Name}({_protocolAdapter.Id})");
        AddLog(BuildProjectProfileLog());
        AddLog(BuildMesPluginLog());
        AddLog(BuildExportPluginLog());
        _jiaLianYiModeTimer = new Timer(_ => PostToUi(CheckJiaLianYiAutoStandardMode), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        _jingWangCraftTimer = new Timer(_ => PostToUi(() => _ = CollectJingWangCraftIfDueAsync()), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        PostToUi(() => _ = AutoConnectConfiguredDevicesAsync());
        if (IsJingWangAccount && _configuration.Mes.OnlineMode) PostToUi(() => _ = StartJingWangSessionAsync());
    }

    public ICommand OpenBasicConfigurationCommand { get; }
    public ICommand OpenCommunicationConfigurationCommand { get; }
    public ICommand OpenEndoscopeCommand { get; }
    public ICommand OpenMesConfigurationCommand { get; }
    public ICommand OpenMesDebugToolCommand { get; }
    public ICommand OpenPluginConfigurationCommand { get; }
    public ICommand OpenKunshanJiaLianYiConfigurationCommand { get; }
    public ICommand OpenTestItemConfigurationCommand { get; }
    public ICommand ImportProjectProfileCommand { get; }
    public ICommand OpenUserConfigurationCommand { get; }
    public ICommand SaveConfigurationCommand { get; }
    public ICommand ConnectDevicesCommand { get; }
    public ICommand DisconnectDevicesCommand { get; }
    public ICommand ToggleScannerConnectionCommand { get; }
    public ICommand ToggleLowerMachineConnectionCommand { get; }
    public ICommand TestScannerCommand { get; }
    public ICommand TestLowerMachineCommand { get; }
    public ICommand ToggleOnlineModeCommand { get; }
    public ICommand SelectStandardPieceModeCommand { get; }
    public ICommand SelectTestPieceModeCommand { get; }
    public ICommand SelectDebugModeCommand { get; }
    public ICommand ToggleLogVisibilityCommand { get; }
    public ICommand ToggleAdminToolsCommand { get; }
    public ICommand ClearYieldCommand { get; }
    public ICommand OpenDataAnalysisCommand { get; }
    public ICommand OpenTestTrendAnalysisCommand { get; }
    public ICommand InitializeFlowCommand { get; }
    public ICommand SimulateLowerMachineStartCommand { get; }
    public ICommand SimulateScanCommand { get; }
    public ICommand SimulatePassCommand { get; }
    public ICommand SimulateFailCommand { get; }
    public ICommand SimulatePopupCommand { get; }
    public ICommand SwitchLoginCommand { get; }
    public ICommand ConfirmPopupCommand { get; }
    public ICommand HelpCommand { get; }

    public ObservableCollection<TestItemViewModel> TestItems { get; }
    public ObservableCollection<string> Logs { get; }
    public ObservableCollection<ProductionRecordViewModel> ProductionRecords { get; }

    public string WindowTitle => string.IsNullOrWhiteSpace(_configuration.WindowTitle) ? "CeHao" : _configuration.WindowTitle;
    public string HeaderTitle => IsJingWangAccount ? $"{JwProduct}-{JwSpec}" : $"{_configuration.Identity.ProductName}-{_configuration.Identity.TestType}-{_configuration.Identity.TestProgram}";
    public string CustomerName => _configuration.Identity.CustomerName;
    public string ProductName => _configuration.Identity.ProductName;
    public string TestType => _configuration.Identity.TestType;
    public string TestProgram => _configuration.Identity.TestProgram;
    public string StationNo
    {
        get => IsKunshanJiaLianYiDedicatedConfiguration
            ? _configuration.Mes.PluginOptions.GetValueOrDefault("Station", string.Empty)
            : _configuration.Identity.StationNo;
        set
        {
            value ??= string.Empty;
            if (string.Equals(StationNo, value, StringComparison.Ordinal))
            {
                return;
            }

            if (IsKunshanJiaLianYiDedicatedConfiguration)
            {
                _configuration.Mes.PluginOptions["Station"] = value;
                if (!_configuration.Export.PluginOptions.TryGetValue("JiaLianYi.Export", out var exportOptions))
                {
                    exportOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    _configuration.Export.PluginOptions["JiaLianYi.Export"] = exportOptions;
                }
                exportOptions["Station"] = value;
            }
            else
            {
                _configuration.Identity.StationNo = value;
            }
            Save();
        }
    }
    public string MachineNo => _configuration.Identity.MachineNo;
    public string MesUserNo => _configuration.Mes.UserNo;
    public string MesPackNo => _configuration.Mes.PackNo;
    public string MesTooling => _configuration.Mes.Tooling;
    public string MesProdNo
    {
        get => _configuration.Mes.ProdNo;
        set
        {
            if (string.Equals(_configuration.Mes.ProdNo, value, StringComparison.Ordinal))
            {
                return;
            }

            _configuration.Mes.ProdNo = value ?? string.Empty;
            Save();
        }
    }
    public bool IsHongXinMesPluginSelected => string.Equals(_configuration.Mes.SelectedPlugin, "HongXin.OA", StringComparison.OrdinalIgnoreCase);
    public string HongXinProductionFlowNo
    {
        get => _configuration.Mes.HongXinProductionFlowNo;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_configuration.Mes.HongXinProductionFlowNo, value, StringComparison.Ordinal))
            {
                return;
            }

            _configuration.Mes.HongXinProductionFlowNo = value;
            Save();
            AddLog($"HongXin MES 生产流程单号已更新：{value}");
        }
    }
    public string MesPlugin => GetMesPluginDisplayName();
    public string OnlineModeText => _configuration.Mes.OnlineMode ? "切换离线" : "切换在线";
    public string LogToggleText => IsLogVisible ? "隐藏日志" : "显示日志";
    public string ScannerSummary => FormatDevice(_configuration.Communication.Scanner);
    public string LowerMachineSummary => FormatDevice(_configuration.Communication.LowerMachine);
    public string LoginDisplay => $"{_authenticationService.CurrentSession.DisplayName} / {CH_V03.App.Localization.UiLanguage.Text(_authenticationService.CurrentSession.RoleText)}";

    public string PopupTitle => _authenticationService.CurrentSession.IsDiagnosticMode ? "设备 / 上位机诊断信息" : "MES / 系统消息提醒";
    public bool CanManageSystem => _authenticationService.CurrentSession.CanManageSystem;
    public bool CanConfigureStation => CanManageSystem || IsYidong2;
    public bool CanConfigureOperatorSettings => _authenticationService.CurrentSession.IsAuthenticated
        && _authenticationService.CurrentSession.Role == UserRole.Operator;
    public bool CanOpenBasicConfiguration => CanManageSystem || CanConfigureOperatorSettings;
    public bool CanOpenCommunicationConfiguration => CanManageSystem || CanConfigureOperatorSettings;
    public bool CanManageTestItems => _authenticationService.CurrentSession.CanManageTestItems;
    public bool CanManageUsers => _authenticationService.CurrentSession.CanManageUsers || IsYidong2;
    public bool CanManageUsersInStandardMode => CanManageUsers && IsStandardConfigurationMode;
    public bool CanImportProjectProfile => _authenticationService.CurrentSession.CanImportProjectProfile;
    public bool CanViewAnalysis => _authenticationService.CurrentSession.CanViewAnalysis;
    public bool CanViewAnalysisInStandardMode => CanViewAnalysis && IsStandardConfigurationMode;
    public bool CanViewHelp => _authenticationService.CurrentSession.IsAuthenticated
        && (IsYidong2 || _authenticationService.CurrentSession.Role != UserRole.Operator);
    public bool CanViewHelpInStandardMode => CanViewHelp && IsStandardConfigurationMode;
    public bool CanUseAdminTools => IsYidong2 || _authenticationService.CurrentSession.Role == UserRole.Administrator;
    public bool IsKunshanJiaLianYiDedicatedConfiguration => _authenticationService.CurrentSession.IsJiaLianYiAccount;
    public bool IsStandardConfigurationMode => !IsKunshanJiaLianYiDedicatedConfiguration;
    public bool CanSelectJiaLianYiRunMode => IsKunshanJiaLianYiDedicatedConfiguration
        && _configuration.Mes.Enabled
        && _configuration.Mes.OnlineMode;
    public bool IsStandardPieceMode => _configuration.User.Jly.RunMode == JiaLianYiRunMode.StandardPiece;
    public bool IsTestPieceMode => _configuration.User.Jly.RunMode == JiaLianYiRunMode.TestPiece;
    public bool IsDebugMode => _configuration.User.Jly.RunMode == JiaLianYiRunMode.Debug;
    public string JiaLianYiRunModeText => GetJiaLianYiRunModeName(_configuration.User.Jly.RunMode);
    public string JiaLianYiRunModeBackground => _configuration.User.Jly.RunMode switch
    {
        JiaLianYiRunMode.TestPiece => "#16A34A",
        JiaLianYiRunMode.Debug => "#DC2626",
        _ => "#FACC15"
    };
    public string JiaLianYiRunModeForeground => IsStandardPieceMode ? "#1F2937" : "White";
    public int TotalCount => PassCount + FailCount;
    public string PassFailText => $"{PassCount} / {FailCount}";
    public double YieldRate => TotalCount == 0 ? 0 : Math.Round(PassCount * 100.0 / TotalCount, 2);
    public string YieldRateText => $"{YieldRate:F2}%";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string ScannerState
    {
        get => _scannerState;
        private set
        {
            if (SetField(ref _scannerState, value))
            {
                OnPropertyChanged(nameof(ScannerStateBrush));
            }
        }
    }

    public string LowerMachineState
    {
        get => _lowerMachineState;
        private set
        {
            if (SetField(ref _lowerMachineState, value))
            {
                OnPropertyChanged(nameof(LowerMachineStateBrush));
            }
        }
    }

    public string MesState
    {
        get => _mesState;
        private set
        {
            if (SetField(ref _mesState, value))
            {
                OnPropertyChanged(nameof(MesStateBrush));
            }
        }
    }

    public string ScannerStateBrush => GetConnectionStateBrush(ScannerState);
    public string LowerMachineStateBrush => GetConnectionStateBrush(LowerMachineState);
    public string MesStateBrush => GetConnectionStateBrush(MesState);

    public string TestState
    {
        get => _testState;
        private set
        {
            if (SetField(ref _testState, value)) OnPropertyChanged(nameof(DashboardTestState));
        }
    }

    public string DashboardTestState
    {
        get
        {
            if (!IsYidong2 || TestState is not ("TEST" or "PASS" or "FAIL")) return TestState;
            if (TestState == "FAIL" || CompanionPhase == "FAIL") return "FAIL";
            if (TestState == "PASS" && CompanionPhase == "PASS") return "PASS";
            return CompanionPhase is "Error" or "Waiting" && TestState == "PASS" ? "WAIT" : "TEST";
        }
    }

    public string CurrentBarcode
    {
        get => _currentBarcode;
        private set => SetField(ref _currentBarcode, value);
    }

    public bool IsManualScanMode => _configuration.User.ManualScanMode;

    public bool IsUsbPosHidScanner =>
        _configuration.Communication.Scanner.ConnectionType == ConnectionType.UsbPosHid;

    private bool IsScanAheadMode => IsManualScanMode && _configuration.User.ScanAheadEnabled;

    public string LastFailItem
    {
        get => _lastFailItem;
        private set => SetField(ref _lastFailItem, value);
    }

    public string TestTime
    {
        get => _testTime;
        private set => SetField(ref _testTime, value);
    }

    public string LastTestResult
    {
        get => _lastTestResult;
        private set => SetField(ref _lastTestResult, value);
    }

    public bool IsLogVisible
    {
        get => _isLogVisible;
        private set
        {
            if (SetField(ref _isLogVisible, value))
            {
                OnPropertyChanged(nameof(LogToggleText));
            }
        }
    }

    public bool IsAdminToolsVisible
    {
        get => _isAdminToolsVisible;
        private set => SetField(ref _isAdminToolsVisible, value);
    }

    public string PopupMessage
    {
        get => _popupMessage;
        private set => SetField(ref _popupMessage, value);
    }

    public bool IsPopupVisible
    {
        get => _isPopupVisible;
        private set => SetField(ref _isPopupVisible, value);
    }

    public bool IsPopupCenteredAndEnlarged => _configuration.Popup.CenterAndEnlarge;

    public bool IsPopupRedAlert => _configuration.Popup.RedAlert;

    public int CurrentConsecutiveNg
    {
        get => _currentConsecutiveNg;
        private set => SetField(ref _currentConsecutiveNg, value);
    }

    public int PassCount
    {
        get => _passCount;
        private set
        {
            if (SetField(ref _passCount, value))
            {
                RaiseYieldProperties();
            }
        }
    }

    public int FailCount
    {
        get => _failCount;
        private set
        {
            if (SetField(ref _failCount, value))
            {
                RaiseYieldProperties();
            }
        }
    }

    private void OpenBasicConfiguration()
    {
        if (!CanOpenBasicConfiguration)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改基础配置，请切换操作员、工程师或管理员账号。");
            return;
        }

        var editable = ConfigurationStore.Clone(_configuration);
        var saved = IsKunshanJiaLianYiDedicatedConfiguration
            ? _configurationDialogService.ShowKunshanJiaLianYiBasicConfiguration(editable, new KunshanJiaLianYiRuntimeActions
            {
                ToggleScanner = ToggleScannerConnectionAsync,
                ToggleLowerMachine = ToggleLowerMachineConnectionAsync,
                ToggleMes = ToggleOnlineModeAsync,
                ScannerState = () => ScannerState,
                LowerMachineState = () => LowerMachineState,
                MesState = () => MesState,
                IsMesOnline = () => _configuration.Mes.OnlineMode
            })
            : _configurationDialogService.ShowBasicConfiguration(editable, CanConfigureStation);
        if (!saved)
        {
            StatusMessage = "已取消基础配置";
            return;
        }

        if (CanConfigureOperatorSettings && !CanConfigureStation && !IsKunshanJiaLianYiDedicatedConfiguration)
        {
            // 即使未来基础配置窗口新增控件，操作员保存时也只能覆盖保存路径。
            var output = editable.Output;
            editable = ConfigurationStore.Clone(_configuration);
            editable.Output = output;
        }

        editable.Communication.LowerMachinePayloadTimeoutSeconds = Math.Max(
            1,
            editable.Communication.LowerMachinePayloadTimeoutSeconds);

        editable.Communication.LowerMachineReceiveBufferMaximumCharacters = Math.Max(
            1,
            editable.Communication.LowerMachineReceiveBufferMaximumCharacters);
        editable.Communication.TestItemRequestRetryIntervalSeconds = Math.Max(
            1,
            editable.Communication.TestItemRequestRetryIntervalSeconds);
        editable.Communication.ScannerWaitTimeoutSeconds = Math.Max(
            1,
            editable.Communication.ScannerWaitTimeoutSeconds);
        var specialValueVisibilityChanged = _configuration.User.HideSpecialItemTestValues
            != editable.User.HideSpecialItemTestValues;
        if (!AcceptJingWangSettings(editable)) return;
        _configuration = editable;
        Save();
        RefreshDerivedState();
        if (specialValueVisibilityChanged)
        {
            ApplySnapshot(_testFlowService.Snapshot(), countYield: false);
        }
        AddLog("基础配置已更新");
    }

    private async Task OpenCommunicationConfigurationAsync()
    {
        if (!CanOpenCommunicationConfiguration)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改通讯设置，请切换操作员、工程师或管理员账号。");
            return;
        }

        var editable = ConfigurationStore.Clone(_configuration);
        if (!_configurationDialogService.ShowCommunicationConfiguration(editable, _communicationTestService))
        {
            StatusMessage = "已取消通讯设置";
            return;
        }

        editable.Communication.ScannerWaitTimeoutSeconds = Math.Max(
            1,
            editable.Communication.ScannerWaitTimeoutSeconds);
        var reconnectLowerMachine = _lowerMachineConnection?.IsOpen == true
            && (_configuration.Communication.LowerMachineHandshakeEnabled != editable.Communication.LowerMachineHandshakeEnabled
                || _configuration.Communication.LowerMachineItemHandshakeEnabled != editable.Communication.LowerMachineItemHandshakeEnabled
                || _configuration.Communication.LowerMachineHandshakeCommand != editable.Communication.LowerMachineHandshakeCommand
                || _configuration.Communication.LowerMachineHandshakeResponse != editable.Communication.LowerMachineHandshakeResponse
                || _configuration.Communication.TestItemRequestCommand != editable.Communication.TestItemRequestCommand);
        if (!AcceptJingWangSettings(editable)) return;
        _configuration = editable;
        Save();
        RefreshDerivedState();
        AddLog("通讯设置已更新");
        if (reconnectLowerMachine)
        {
            await DisconnectLowerMachineAsync();
            RefreshDerivedState();
            AddLog("下位机连接判据或命令已更新，正在按新设置重新连接");
            await ConnectLowerMachineAsync();
        }
        await ReconnectScannerAfterCommunicationModeChangeAsync("扫码枪参数已更新，正在按新参数重新连接");
    }

    private async Task OpenEndoscopeAsync()
    {
        if (!CanOpenCommunicationConfiguration)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改内窥镜设置，请切换操作员、工程师或管理员账号。");
            return;
        }

        var editable = ConfigurationStore.Clone(_configuration);
        if (!_configurationDialogService.ShowEndoscope(editable))
        {
            StatusMessage = "已取消内窥镜设置";
            return;
        }

        editable.Communication.Scanner.ConnectionType = ConnectionType.CameraQr;
        editable.Communication.Scanner.Name = "内窥镜二维码识别";
        editable.Communication.Scanner.CameraIndex = Math.Max(0, editable.Communication.Scanner.CameraIndex);
        editable.Communication.Scanner.CameraWidth = Math.Max(160, editable.Communication.Scanner.CameraWidth);
        editable.Communication.Scanner.CameraHeight = Math.Max(120, editable.Communication.Scanner.CameraHeight);
        editable.Communication.Scanner.CameraFrameRate = Math.Max(1, editable.Communication.Scanner.CameraFrameRate);
        editable.Communication.Scanner.CameraScanTimeoutMilliseconds = Math.Max(300, editable.Communication.Scanner.CameraScanTimeoutMilliseconds);
        if (!AcceptJingWangSettings(editable)) return;
        _configuration = editable;
        Save();
        RefreshDerivedState();
        await ReconnectScannerAfterCommunicationModeChangeAsync(
            "内窥镜二维码识别已启用，正在重新连接扫码设备",
            forceReconnect: true);
    }

    private void OpenMesConfiguration()
    {
        if (!CanManageSystem)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改 MES 设置，请切换工程师或管理员账号。");
            return;
        }

        var editable = ConfigurationStore.Clone(_configuration);
        if (!_configurationDialogService.ShowMesConfiguration(editable))
        {
            StatusMessage = "已取消 MES 设置";
            return;
        }

        if (!AcceptJingWangSettings(editable)) return;
        _configuration = editable;
        Save();
        RefreshDerivedState();
        AddLog("MES 设置已更新");
    }

    private void OpenMesDebugTool()
    {
        if (!CanManageSystem)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能使用 MES 调试工具，请切换工程师或管理员账号。");
            return;
        }

        _configurationDialogService.ShowMesDebugTool(_configuration);
    }

    private void OpenPluginConfiguration()
    {
        if (!CanConfigureStation)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改插件配置，请切换工程师或管理员账号。");
            return;
        }

        var editable = ConfigurationStore.Clone(_configuration);
        if (!_configurationDialogService.ShowPluginConfiguration(editable))
        {
            StatusMessage = "已取消插件配置";
            return;
        }

        if (!AcceptJingWangSettings(editable)) return;
        _configuration = editable;
        Save();
        RefreshDerivedState();
        AddLog("插件配置已更新");
    }

    private void OpenKunshanJiaLianYiConfiguration()
    {
        if (!CanManageSystem)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改昆山嘉联益配置，请切换工程师或管理员账号。");
            return;
        }

        var editable = ConfigurationStore.Clone(_configuration);
        if (!_configurationDialogService.ShowKunshanJiaLianYiConfiguration(editable))
        {
            StatusMessage = "已取消昆山嘉联益配置";
            return;
        }

        ApplyKunshanJiaLianYiDedicatedDefaults(editable);
        if (!AcceptJingWangSettings(editable)) return;
        _configuration = editable;
        Save();
        RefreshDerivedState();
        AddLog("昆山嘉联益配置已更新");
    }

    private void OpenTestItemConfiguration()
    {
        if (!CanManageTestItems)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改测试项配置，请切换管理员账号。");
            return;
        }

        var editable = ConfigurationStore.Clone(_configuration.TestItems);
        if (!_configurationDialogService.ShowTestItemConfiguration(editable))
        {
            StatusMessage = "已取消测试项配置";
            return;
        }

        editable.EnsureDefaultItems();
        _configuration.TestItems = editable;
        Save();
        ApplySnapshot(_testFlowService.LoadConfiguredTestItems(_configuration.TestItems.Items), countYield: false);
        AddLog("测试项配置已更新");
    }

    private void ImportProjectProfile()
    {
        if (!CanImportProjectProfile)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能导入客户方案，请切换管理员账号。");
            return;
        }

        if (_projectProfiles.Count == 0)
        {
            _configurationDialogService.ShowAlert("没有客户方案", "profiles 目录下没有可导入的客户方案。");
            return;
        }

        if (!_configurationDialogService.ShowProjectProfileImport(_projectProfiles, out var profile) || profile is null)
        {
            StatusMessage = "已取消导入客户方案";
            return;
        }

        var currentAccounts = _configuration.User.Accounts;
        var imported = ConfigurationStore.Clone(profile.Configuration);
        imported.User.Accounts = currentAccounts;
        imported.Mes.OnlineMode = false;
        imported.Compatibility.ProjectProfileId = profile.Id;
        imported.Compatibility.ProtocolAdapterId = profile.ProtocolAdapterId;

        imported.User.EnsureDefaultAccounts();
        imported.TestItems.EnsureDefaultItems();
        _authenticationService.ApplyAccountMode(imported);
        ApplyKunshanJiaLianYiDedicatedDefaults(imported);

        if (!AcceptJingWangSettings(imported)) return;
        _configuration = imported;
        NormalizeScanMode(_configuration.User);
        Save();
        RefreshDerivedState();
        ApplySnapshot(_testFlowService.LoadConfiguredTestItems(_configuration.TestItems.Items), countYield: false);
        AddLog($"已导入项目方案：{profile.Name}({profile.Id})，MES 已切换为离线模式");
    }

    private void OpenUserConfiguration()
    {
        if (!CanManageUsers)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改用户配置，请切换工程师或管理员账号。");
            return;
        }

        var editable = ConfigurationStore.Clone(_configuration.User);
        if (IsKunshanJiaLianYiDedicatedConfiguration)
        {
            editable.SaveDailyQuantity = true;
        }

        var canManageAccounts = CanUseAdminTools;
        var saved = IsKunshanJiaLianYiDedicatedConfiguration
            ? _configurationDialogService.ShowKunshanJiaLianYiUserConfiguration(editable, _configuration.Compatibility)
            : _configurationDialogService.ShowUserConfiguration(editable, _configuration.Compatibility, canManageAccounts);
        if (!saved)
        {
            StatusMessage = "已取消用户配置";
            return;
        }

        if (!canManageAccounts || IsKunshanJiaLianYiDedicatedConfiguration)
        {
            editable.Accounts = ConfigurationStore.Clone(_configuration.User.Accounts);
        }

        var wasSavingDailyQuantity = _configuration.User.SaveDailyQuantity;
        if (IsKunshanJiaLianYiDedicatedConfiguration)
        {
            editable.SaveDailyQuantity = true;
        }

        var switchedToManualScan = !_configuration.User.ManualScanMode && editable.ManualScanMode && !editable.JingWangKeyboardSn;
        var switchedToAutomaticScan = _configuration.User.ManualScanMode && editable.AutoScan;
        _configuration.User = editable;
        NormalizeScanMode(_configuration.User);
        if (switchedToManualScan)
        {
            _configuration.Communication.Scanner.ConnectionType = ConnectionType.UsbPosHid;
            AddLog("已切换手动扫码，扫码枪通讯方式已自动设为 USB POS HID");
        }
        else if (switchedToAutomaticScan)
        {
            _configuration.Communication.Scanner.ConnectionType = ConnectionType.SerialPort;
            AddLog("已切换自动扫码，扫码枪通讯方式已自动设为串口");
        }
        _scanAheadStatus = ScanAheadStatus.None;
        if (_configuration.User.SaveDailyQuantity)
        {
            PersistDailyYieldStatistics();
        }
        else if (wasSavingDailyQuantity)
        {
            ClearSavedDailyYieldStatistics();
        }
        Save();
        RefreshDerivedState();
        if (switchedToManualScan || switchedToAutomaticScan)
        {
            _ = ReconnectScannerAfterCommunicationModeChangeAsync();
        }
        AddLog("用户配置已更新");
    }

    private void SwitchLogin()
    {
        if (_companionMaintenanceBusy) { _configurationDialogService.ShowAlert("正在维护陪测板", "请等待烧录或写入完成后切换账号"); return; }
        if (System.Windows.Application.Current is App)
        {
            if (TestState is "SCAN" or "READY" or "TEST" || _waitingForItemHeader || _pendingJiaLianYiMesUploadCount > 0)
            {
                _configurationDialogService.ShowAlert("暂不能切换", "请等待当前测试及数据上传完成后切换账号。");
                return;
            }
            Save();
            App.RestartForLogin();
            return;
        }

        if (string.Equals(
                _authenticationService.CurrentSession.UserName,
                "huaqin",
                StringComparison.OrdinalIgnoreCase))
        {
            // 华勤账号的切换登录即恢复本机已记住的华勤会话，不再重复要求输入凭据。
            _configuration.AutoLoginUserName = "huaqin";
            _configuration.DefaultOperatorLogin = true;
            Save();
            ShowLogin();
            return;
        }

        ShowLogin(forcePrompt: true);
    }

    public bool ShowLogin(bool forcePrompt = false)
    {
        if (_configuration.DefaultOperatorLogin && !forcePrompt)
        {
            var autoLoginUserName = string.IsNullOrWhiteSpace(_configuration.AutoLoginUserName)
                ? "operator"
                : _configuration.AutoLoginUserName;
            if (!_authenticationService.LoginAsConfiguredUser(_configuration.User, autoLoginUserName, out var message))
            {
                _configurationDialogService.ShowAlert("自动登录失败", message);
                return false;
            }

            ClearSessionPopups();
            ApplySessionAccountMode();
            AddLog(message);
            RaiseSessionProperties();
            return true;
        }

        if (_configurationDialogService.ShowLogin(_authenticationService, _configuration.User))
        {
            if (string.Equals(
                _authenticationService.CurrentSession.UserName,
                "huaqin",
                StringComparison.OrdinalIgnoreCase))
            {
                _configuration.AutoLoginUserName = "huaqin";
                _configuration.DefaultOperatorLogin = true;
            }

            ApplySessionAccountMode();
            ClearSessionPopups();
            AddLog($"用户登录：{LoginDisplay}");
            RaiseSessionProperties();
            return true;
        }

        return false;
    }

    private void ApplySessionAccountMode()
    {
        _authenticationService.ApplyAccountMode(_configuration);
        ApplyKunshanJiaLianYiDedicatedDefaults(_configuration);
        ApplyJiaLianYiStartupProductionMode(_configuration);
        _pendingJlyRetestFailure = null;
        Save();
        RefreshDerivedState();
    }

    private void OpenDataAnalysis()
    {
        if (!CanViewAnalysis)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能查看数据分析，请切换技术员、工程师或管理员账号。");
            return;
        }

        _configurationDialogService.ShowDataAnalysis(ProductionRecords, PassCount, FailCount, CurrentConsecutiveNg);
    }

    private void OpenTestTrendAnalysis()
    {
        if (!CanViewAnalysis)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能查看测试趋势分析，请切换技术员、工程师或管理员账号。");
            return;
        }

        _configurationDialogService.ShowTestTrendAnalysis(ProductionRecords);
    }

    private async Task ConnectDevicesAsync()
    {
        await DisconnectDevicesAsync();
        await ConnectScannerAsync();
        await ConnectLowerMachineAsync();
    }

    private async Task AutoConnectConfiguredDevicesAsync()
    {
        var failedDevices = new List<string>();

        if (_configuration.Communication.AutoConnectScannerOnStartup
            && !await ConnectScannerAsync())
        {
            failedDevices.Add("扫码枪");
        }

        if (_configuration.Communication.AutoConnectLowerMachineOnStartup
            && !await ConnectLowerMachineAsync())
        {
            failedDevices.Add("下位机");
        }

        if (failedDevices.Count == 0)
        {
            return;
        }

        var message = $"启动自动连接失败：{string.Join("、", failedDevices)}。请检查设备连接和通讯配置。";
        AddLog(message);
        await ShowRuntimePopupAsync(message, force: true, requiresConfirmation: false);
    }

    private async Task ToggleScannerConnectionAsync()
    {
        if (_scannerConnection?.IsOpen == true)
        {
            await DisconnectScannerAsync();
            RefreshDerivedState();
            AddLog("扫码枪连接已断开");
            return;
        }

        await ConnectScannerAsync();
    }

    private async Task ToggleLowerMachineConnectionAsync()
    {
        if (_lowerMachineConnection?.IsOpen == true)
        {
            await DisconnectLowerMachineAsync();
            RefreshDerivedState();
            AddLog("下位机连接已断开");
            return;
        }

        await ConnectLowerMachineAsync();
    }

    private async Task<bool> ConnectScannerAsync()
    {
        try
        {
            _scannerConnection = _deviceConnectionFactory.Create(_configuration.Communication.Scanner);
            _scannerConnection.DataReceived += ScannerConnection_DataReceived;
            await _scannerConnection.OpenAsync();
            ScannerState = _configuration.Communication.Scanner.ConnectionType == ConnectionType.Disabled ? "已禁用" : "已连接";
            AddLog($"扫码枪连接完成：{ScannerSummary}");
            return true;
        }
        catch (Exception ex)
        {
            if (_scannerConnection is not null)
            {
                _scannerConnection.DataReceived -= ScannerConnection_DataReceived;
                await _scannerConnection.DisposeAsync();
                _scannerConnection = null;
            }

            ScannerState = "异常";
            AddDiagnosticLog("扫码设备 / 连接失败", $"扫码枪连接失败：{ex.Message}", "检查供电、连接线、端口、波特率或网络地址。");
            return false;
        }
    }

    private async Task<bool> ConnectLowerMachineAsync()
    {
        try
        {
            ResetLowerMachineReceiveBuffer();
            CancelTestItemRequestRetry();
            _waitingForItemHeader = true;
            _startTestAfterItemHeader = false;
            _discardLowerMachineDataUntilNextStartSignal = false;
            _lowerMachineConnection = _deviceConnectionFactory.Create(_configuration.Communication.LowerMachine);
            _lowerMachineConnection.DataReceived += LowerMachineConnection_DataReceived;
            if (_lowerMachineConnection is IDeviceConnectionDiagnostics diagnostics)
            {
                diagnostics.CommunicationError += LowerMachineConnection_CommunicationError;
            }
            await _lowerMachineConnection.OpenAsync();
            _lowerMachineProtocolConfirmed = false;
            _lowerMachineItemRequestSent = false;
            if (_configuration.Communication.LowerMachine.ConnectionType == ConnectionType.Disabled)
            {
                LowerMachineState = "已禁用";
                return true;
            }

            var requireIsOpen = _configuration.Communication.LowerMachineHandshakeEnabled;
            var requireItemHeader = _configuration.Communication.LowerMachineItemHandshakeEnabled;
            _lowerMachineProtocolConfirmed = !requireIsOpen && !requireItemHeader;
            LowerMachineState = _lowerMachineProtocolConfirmed ? "已连接" : "待协议确认";
            if (requireItemHeader)
            {
                _lowerMachineItemHandshakeReply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            AddLog($"下位机端口已打开：{LowerMachineSummary}");
            if (requireIsOpen)
            {
                if (!await ConfirmLowerMachineHandshakeAsync())
                {
                    await DisconnectLowerMachineAsync();
                    LowerMachineState = "握手失败";
                    return false;
                }
            }
            if (!requireIsOpen && !requireItemHeader)
            {
                AddLog("未启用连接握手；以串口打开作为连接成功条件");
            }
            if (requireItemHeader)
            {
                LowerMachineState = "待表头确认";
            }
            StartTestItemRequestRetry(waitForAutomaticHeader: false);
            if (requireItemHeader)
            {
                if (!await _lowerMachineItemHandshakeReply!.Task)
                {
                    await DisconnectLowerMachineAsync();
                    LowerMachineState = "表头握手失败";
                    return false;
                }

                _lowerMachineItemHandshakeReply = null;
            }
            return true;
        }
        catch (Exception ex)
        {
            _lowerMachineItemHandshakeReply?.TrySetResult(false);
            _lowerMachineItemHandshakeReply = null;
            CancelTestItemRequestRetry();
            if (_lowerMachineConnection is not null)
            {
                _lowerMachineConnection.DataReceived -= LowerMachineConnection_DataReceived;
                if (_lowerMachineConnection is IDeviceConnectionDiagnostics diagnostics)
                {
                    diagnostics.CommunicationError -= LowerMachineConnection_CommunicationError;
                }
                await _lowerMachineConnection.DisposeAsync();
                _lowerMachineConnection = null;
            }

            LowerMachineState = "异常";
            AddDiagnosticLog("下位机 / 连接失败", $"下位机连接失败：{ex.Message}", "检查下位机供电、通讯参数及端口占用。");
            return false;
        }
    }

    private async Task<bool> ConfirmLowerMachineHandshakeAsync()
    {
        var command = DecodeCommand(_configuration.Communication.LowerMachineHandshakeCommand);
        var expectedResponse = NormalizeIncomingText(_configuration.Communication.LowerMachineHandshakeResponse);
        if (string.IsNullOrWhiteSpace(command) || string.IsNullOrWhiteSpace(expectedResponse))
        {
            throw new InvalidOperationException("下位机握手命令和期望回复均不能为空");
        }

        var connection = _lowerMachineConnection
            ?? throw new InvalidOperationException("下位机尚未打开，不能发送握手命令");
        var pendingReply = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        _lowerMachineHandshakeParser = new LowerMachineHandshakeReplyParser(expectedResponse);
        _lowerMachineHandshakeReply = pendingReply;
        try
        {
            await connection.SendStringAsync(command);
            AddLog($"sendLowerCom:{FormatCommandForLog(command)}（连接握手）");
            var timeoutMilliseconds = Math.Max(1, _configuration.Communication.LowerMachine.TimeoutMilliseconds);
            if (await Task.WhenAny(pendingReply.Task, Task.Delay(timeoutMilliseconds)) != pendingReply.Task)
            {
                AddDiagnosticLog("下位机 / 握手超时", $"发送 {FormatCommandForLog(command)} 后 {timeoutMilliseconds} ms 未收到回复", "确认下位机支持握手命令，检查通讯参数和接线。");
                return false;
            }

            var actualResponse = await pendingReply.Task;
            if (!string.Equals(actualResponse, expectedResponse, StringComparison.Ordinal))
            {
                AddDiagnosticLog("下位机 / 握手回复错误", $"期望回复 {expectedResponse}，实际收到 {actualResponse}", "确认下位机握手回复内容及通讯配置。");
                return false;
            }

            _lowerMachineProtocolConfirmed = true;
            LowerMachineState = "握手成功";
            AddLog($"下位机握手成功：{FormatCommandForLog(command)} → {actualResponse}");
            return true;
        }
        finally
        {
            if (ReferenceEquals(_lowerMachineHandshakeReply, pendingReply))
            {
                _lowerMachineHandshakeReply = null;
            }
            _lowerMachineHandshakeParser = null;
        }
    }

    private async Task DisconnectDevicesAsync()
    {
        ResetScannerReceiveBuffer();
        await DisconnectScannerAsync();
        await DisconnectLowerMachineAsync();

        RefreshDerivedState();
        AddLog("设备连接已断开");
    }

    private async Task DisconnectScannerAsync()
    {
        ResetScannerReceiveBuffer();
        CancelScannerWaitTimeout();
        if (_scannerConnection is not null)
        {
            _scannerConnection.DataReceived -= ScannerConnection_DataReceived;
            await _scannerConnection.DisposeAsync();
            _scannerConnection = null;
        }
    }

    private async Task ReconnectScannerAfterCommunicationModeChangeAsync(
        string reconnectMessage = "扫码枪通讯方式已切换，正在重新连接",
        bool forceReconnect = false)
    {
        if (!forceReconnect && _scannerConnection?.IsOpen != true)
        {
            return;
        }

        await DisconnectScannerAsync();
        RefreshDerivedState();
        AddLog(reconnectMessage);
        var connected = await ConnectScannerAsync();
        RefreshDerivedState();
        if (connected)
        {
            AddLog("扫码设备已按新配置自动重连，可直接等待下位机 DM 触发扫码");
        }
    }

    private async Task DisconnectLowerMachineAsync()
    {
        _lowerMachineHandshakeReply?.TrySetResult(string.Empty);
        _lowerMachineHandshakeReply = null;
        _lowerMachineHandshakeParser = null;
        _lowerMachineItemHandshakeReply?.TrySetResult(false);
        _lowerMachineItemHandshakeReply = null;
        _lowerMachineProtocolConfirmed = false;
        _lowerMachineItemRequestSent = false;
        ResetLowerMachineReceiveBuffer();
        CancelScannerWaitTimeout();
        CancelTestItemRequestRetry();
        _waitingForItemHeader = false;
        _startTestAfterItemHeader = false;
        _discardLowerMachineDataUntilNextStartSignal = false;
        _scanAheadStatus = ScanAheadStatus.None;
        if (_lowerMachineConnection is not null)
        {
            _lowerMachineConnection.DataReceived -= LowerMachineConnection_DataReceived;
            if (_lowerMachineConnection is IDeviceConnectionDiagnostics diagnostics)
            {
                diagnostics.CommunicationError -= LowerMachineConnection_CommunicationError;
            }
            await _lowerMachineConnection.DisposeAsync();
            _lowerMachineConnection = null;
        }
    }

    private async Task TestScannerAsync()
    {
        var result = await _communicationTestService.TestAsync(_configuration.Communication.Scanner);
        ScannerState = result.IsSuccess ? "已连接" : "异常";
        StatusMessage = result.Message;
        AddLog(result.Message);
    }

    private async Task TestLowerMachineAsync()
    {
        if (_lowerMachineConnection?.IsOpen == true)
        {
            StatusMessage = _lowerMachineProtocolConfirmed
                ? _configuration.Communication.LowerMachineHandshakeEnabled || _configuration.Communication.LowerMachineItemHandshakeEnabled
                    ? "下位机连接握手已通过"
                    : "串口已打开，按当前配置判定已连接"
                : "下位机连接判据尚未通过";
            AddLog(StatusMessage);
            return;
        }

        var result = await _communicationTestService.TestAsync(_configuration.Communication.LowerMachine);
        LowerMachineState = _configuration.Communication.LowerMachine.ConnectionType == ConnectionType.Disabled
            ? "已禁用"
            : result.IsSuccess ? "未连接" : "异常";
        StatusMessage = result.IsSuccess
            ? $"{result.Message}；此处仅测试端口。请在主界面连接以执行已勾选的连接判据。"
            : result.Message;
        AddLog(StatusMessage);
    }

    private void ScannerConnection_DataReceived(object? sender, DeviceDataReceivedEventArgs e)
    {
        PostToUi(() => BufferScannerText(e.Text));
    }

    public Task AcceptUsbPosHidKeyboardInputAsync(string text)
    {
        return IsUsbPosHidScanner
            ? HandleScannerTextAsync(text)
            : Task.CompletedTask;
    }

    private void LowerMachineConnection_DataReceived(object? sender, DeviceDataReceivedEventArgs e)
    {
        PostToUi(() =>
        {
            if (ReferenceEquals(sender, _lowerMachineConnection) && _lowerMachineConnection?.IsOpen == true)
            {
                if (_lowerMachineHandshakeReply is { } pendingReply)
                {
                    var parsed = _lowerMachineHandshakeParser?.Add(e.Text)
                        ?? new HandshakeReplyParseResult(HandshakeReplyStatus.Rejected, "[握手解析器不可用]");
                    if (parsed.Status == HandshakeReplyStatus.Pending)
                    {
                        return;
                    }

                    AddLog($"RecLowerHandshake:{parsed.Received}");
                    pendingReply.TrySetResult(parsed.Received);
                    if (parsed.Status == HandshakeReplyStatus.Accepted)
                    {
                        _lowerMachineHandshakeReply = null;
                        _lowerMachineHandshakeParser = null;
                        if (!string.IsNullOrEmpty(parsed.RemainingPayload))
                        {
                            HandleLowerMachineText(parsed.RemainingPayload);
                        }
                    }
                }
                else
                {
                    HandleLowerMachineText(e.Text);
                }
            }
        });
    }

    private void LowerMachineConnection_CommunicationError(object? sender, DeviceCommunicationErrorEventArgs e)
    {
        PostToUi(() =>
        {
            if (!ReferenceEquals(sender, _lowerMachineConnection))
            {
                return;
            }

            _lowerMachineProtocolConfirmed = false;
            LowerMachineState = "异常";
            _lowerMachineHandshakeReply?.TrySetResult(string.Empty);
            _lowerMachineItemHandshakeReply?.TrySetResult(false);
            AddDiagnosticLog(
                "下位机 / 串口接收异常",
                e.Message,
                "检查 USB 转串口、线缆和供电；重新连接后重试。若反复发生，请保留运行日志。");
        });
    }

    private async Task HandleScannerTextAsync(string text, bool keyboard = false)
    {
        if (KeyboardSnMode && !keyboard) return;
        if (IsJingWangAccount && (_jwScanning || _jwProcessing || IsPopupVisible || TestState == "TEST")) return;
        if (IsJingWangAccount) { _jwScanning = true; _scanAheadStatus = ScanAheadStatus.None; OnPropertyChanged(nameof(CanEditJingWangFields)); }
        try
        {
            await HandleScannerTextCoreAsync(text);
        }
        catch (Exception ex)
        {
            AddDiagnosticLog("上位机 / 扫码处理异常", $"扫码数据处理失败：{ex.GetType().Name}：{ex.Message}", "检查条码、配置及相关服务日志。");
        }
        finally { _jwScanning = false; OnPropertyChanged(nameof(CanEditJingWangFields)); }
    }

    public ICommand ReleaseFixtureCommand { get; }
    public ICommand DisconnectCompanionCommand { get; }
    public ICommand CheckCompanionCommand { get; }
    public ICommand BurnCompanionCommand { get; }
    public ICommand WriteCompanionCommand { get; }
    private bool _companionMaintenanceBusy;
    public bool CompanionMaintenanceBusy => _companionMaintenanceBusy;
    public bool CanMaintainCompanion => IsYidong2 && _companion.Connected && !_companionMaintenanceBusy
        && _jointRun == null && !_jointFinishing && _testFlowService.Snapshot().State != TestFlowState.Testing;

    private async Task MaintainCompanionAsync(bool firmware)
    {
        if (!CanMaintainCompanion) return;
        _companionMaintenanceBusy = true;
        OnPropertyChanged(nameof(CanMaintainCompanion));
        var operation = firmware ? "烧录固件" : "写入配置";
        try
        {
            var writes = firmware ? AwinicMaintenance.ReadFirmware() : AwinicMaintenance.ReadRegisters();
            AddLog($"陪测板{operation}开始，共 {writes.Count} 项");
            for (int index = 0; index < writes.Count; index++)
            {
                var write = writes[index];
                await _companion.WriteAsync(write.Address, write.Value);
                if (index % 16 == 0 || index == writes.Count - 1)
                    CompanionStatus = $"{operation} {index + 1}/{writes.Count}";
            }
            CompanionStatus = $"{operation}完成";
        }
        catch (Exception ex) { CompanionStatus = $"{operation}失败：{ex.Message}"; }
        finally
        {
            _companionMaintenanceBusy = false;
            OnPropertyChanged(nameof(CanMaintainCompanion));
        }
        AddLog(CompanionStatus);
    }
    private readonly AwinicUsb _companion = new();
    private string _companionStatus = "未连接";
    public string CompanionStatus { get => _companionStatus; private set => SetField(ref _companionStatus, value); }
    public void DisconnectCompanion()
    {
        if (_companionMaintenanceBusy || (_jointFinishing && !CompanionRunning))
        {
            AddLog("陪测板维护或联合结果提交中，请等待完成后断开");
            return;
        }
        AbortJointRun("操作员断开陪测板");
        _companion.Dispose();
        OnPropertyChanged(nameof(CanMaintainCompanion));
        CompanionStatus = "未连接";
    }
    private async Task CheckCompanionAsync()
    {
        if (_companionMaintenanceBusy || CompanionRunning || _jointFinishing || _companionConnecting)
        {
            AddLog("陪测或联合结果提交中，请先断开并中止本轮后再连接");
            return;
        }
        _companionConnecting = true;
        try
        {
            // 清除旧版本遗留的已结束任务，重连前等待旧 USB 操作完全退出。
            AbortJointRun("重新连接前清理未完成的联合任务");
            if (_companionWorkerTask is not null) await _companionWorkerTask;
            StatusMessage = await _companion.ConnectAsync();
        }
        catch (Exception ex) { StatusMessage = $"陪测板 USB 连接失败：{ex.Message}"; }
        finally { _companionConnecting = false; }
        CompanionStatus = StatusMessage;
        OnPropertyChanged(nameof(CanMaintainCompanion));
        AddLog(StatusMessage);
    }
    private TaskCompletionSource<List<TestItemResult>?>? _jointRun;
    private bool _jointFinishing;
    private bool _companionConnecting;
    private CancellationTokenSource? _companionRunCts;
    private Task? _companionWorkerTask;
    private bool CompanionRunning => _jointRun is { Task.IsCompleted:false };

    private void AbortJointRun(string reason)
    {
        var run = _jointRun;
        if (run is null) return;
        var barcode = _jointBarcode;
        // 先解除当前任务身份，旧任务的回调随后只能退出，不能修改下一轮。
        _jointRun = null;
        _jointBarcode = null;
        _jointFinishing = false;
        _companionRunCts?.Cancel();
        run.TrySetResult(null);
        ResetLowerMachineReceiveBuffer();
        CancelScannerWaitTimeout();
        _lowerMachineStartSignalTime = null;
        _scanAheadStatus = ScanAheadStatus.None;
        _discardLowerMachineDataUntilNextStartSignal = true;
        if (_testFlowService.Snapshot().State == TestFlowState.Testing)
            ApplySnapshot(_testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration)), countYield: false);
        CompanionPhase = "Error";
        CompanionStatus = reason;
        StatusMessage = $"联合测试已中止：{reason}；重连并复位治具后等待新的 DM";
        AddLog($"联合测试 SN={barcode} 已中止：{reason}；旧任务已清理，本轮不提交联合结果");
        OnPropertyChanged(nameof(CanMaintainCompanion));
    }
    public ObservableCollection<TestItemResult> CompanionRows { get; } = new(AwinicForceTest.Names(Yidong2Exporter.UsesVersion3).Select((name,i)=>new TestItemResult {Index=i+1, ItemName=name}));
    private string _companionPhase="Waiting";
    public string CompanionPhase
    {
        get => _companionPhase;
        private set
        {
            if (!SetField(ref _companionPhase, value)) return;
            OnPropertyChanged(nameof(DashboardTestState));
            OnPropertyChanged(nameof(CompanionResultText));
            OnPropertyChanged(nameof(CompanionResultBrush));
            OnPropertyChanged(nameof(CompanionFirstFailItem));
        }
    }
    public string CompanionFirstFailItem => CompanionPhase == "FAIL"
        ? CompanionRows.OrderBy(row => row.Index).FirstOrDefault(row => !row.IsPass)?.ItemName ?? string.Empty
        : string.Empty;
    public string CompanionResultText => CompanionPhase switch
    {
        "PASS" => "PASS",
        "FAIL" => "FAIL",
        "Error" => "异常",
        "Waiting" => "等待",
        _ => "测试中"
    };
    public string CompanionResultBrush => CompanionPhase switch
    {
        "PASS" => "#16A34A",
        "FAIL" or "Error" => "#DC2626",
        _ => "#CA8A04"
    };
    private int _companionPass, _companionCount;
    public string CompanionStats => $"测试 {_companionCount}　 PASS {_companionPass}　 FAIL {_companionCount-_companionPass}　 良率 {(_companionCount==0?0:100.0*_companionPass/_companionCount):F1}%";
    public ICommand ReadCompanionIdCommand { get; }
    private async Task ReadCompanionIdAsync()
    {
        if (_companionMaintenanceBusy || CompanionRunning) return;
        try { CompanionStatus=$"Chip ID：0x{await _companion.ReadAsync(0xff10):X8}"; }
        catch(Exception ex){CompanionStatus=ex.Message;}
        AddLog(CompanionStatus);
    }
    private async Task MeasureCompanionAsync(TaskCompletionSource<List<TestItemResult>?> completion,string barcode,
        CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(_jointRun, completion)) return;
        var cancellation = _companionRunCts;
        try
        {
            CompanionRows.Clear();
            var version3 = Yidong2Exporter.UsesVersion3;
            AddLog($"陪测 SN={barcode}：采用奕东 V1.0.0.{(version3 ? 3 : 1)} 配置和判定");
            foreach(var name in AwinicForceTest.Names(version3)) CompanionRows.Add(new(){Index=CompanionRows.Count+1,ItemName=name});
            var rows=await new AwinicForceTest(_companion, version3).RunAsync(
                stage => { if (ReferenceEquals(_jointRun, completion)) CompanionPhase = stage; },
                row => { if (ReferenceEquals(_jointRun, completion)) CompanionRows[row.Index-1] = row; },
                cancellationToken);
            if (!ReferenceEquals(_jointRun, completion)) return;

            var pass=rows.All(row=>row.IsPass);
            CompanionPhase=pass?"PASS":"FAIL";
            _companionCount++;
            if(pass) _companionPass++;
            OnPropertyChanged(nameof(CompanionStats));
            AddLog($"陪测 SN={barcode}：{CompanionPhase}");
            completion.TrySetResult(rows);
        }
        catch(Exception ex)
        {
            if (!ReferenceEquals(_jointRun, completion)) return;
            var reason = $"{ex.GetType().Name} (0x{ex.HResult:X8})：{(string.IsNullOrWhiteSpace(ex.Message) ? "USB 通讯中断或操作已取消" : ex.Message)}";
            AddLog($"陪测 SN={barcode} 中止：{reason}");
            AbortJointRun(reason);
            _companion.Dispose();
        }
        finally
        {
            if (ReferenceEquals(_companionRunCts, cancellation)) _companionRunCts = null;
            cancellation?.Dispose();
        }
    }
    public bool IsYidong2 => _authenticationService.CurrentSession.IsYidongAccount;
    private string? _jointBarcode;


    private bool _releaseBusy;
    private async Task ReleaseFixtureAsync()
    {
        if (!IsYidong2 || _companionMaintenanceBusy || _releaseBusy || CompanionRunning || _jointFinishing || _testFlowService.Snapshot().State == TestFlowState.Testing)
        {
            AddLog("正在测试或提交结果，暂不能主动退料");
            return;
        }
        if (System.Windows.MessageBox.Show("确认当前治具可以退料？未完成的联合测试将终止。", "主动退料",
            System.Windows.MessageBoxButton.YesNo) != System.Windows.MessageBoxResult.Yes) return;
        _releaseBusy = true;
        try
        {

            if (await SendLowerMachineAsync(_protocolAdapter.TestOverCommand))
            {
                AddLog($"已发送 TestOver 主动退料，SN={_jointBarcode}");
                _jointBarcode = null;
                _jointRun=null;
                    OnPropertyChanged(nameof(CanMaintainCompanion));
            }
        }
        finally { _releaseBusy = false; }
    }

    private async Task HandleScannerTextCoreAsync(string text)
    {
        var barcode = NormalizeIncomingText(text);
        if (string.IsNullOrWhiteSpace(barcode))
        {
            AddLog("扫码枪空数据已忽略");
            return;
        }

        if (IsYidong2 && (_jointRun != null || _jointFinishing)) { AddLog("联合测试进行中，忽略新扫码"); return; }
        CancelScannerWaitTimeout();
        AddLog($"RecScanner:{barcode}");
        if (!KeyboardSnMode) await StopScannerAsync();

        await WaitForJiaLianYiMesUploadAsync();

        if (!_configuration.Identity.AllowContinuousRepeatTest
            && string.Equals(barcode, _lastTestStartedBarcode, StringComparison.OrdinalIgnoreCase))
        {
            const string message = "条码与上一笔测试相同，未启用连续重复测试，已阻止本次测试";
            StatusMessage = message;
            AddLog($"{message}：{barcode}");
            await ShowRuntimePopupAsync(message, KeyboardSnMode);
            return;
        }

        var snapshot = _testFlowService.AcceptBarcode(barcode, TestFlowOptions.FromConfiguration(_configuration));
        if (IsYidong2 && _jointRun == null && snapshot.State == TestFlowState.Ready)
        {

            _jointBarcode = barcode;
        }
        ApplySnapshot(snapshot, countYield: false);

        if (snapshot.State == TestFlowState.Ready)
        {
            if (UseJingWangRecords)
            {
                _jingWangPrecheckBarcode = barcode;
                _jingWangPrecheckStatus = "未过站";
                var scanResult = await _mesRuntimeService.RecordJingWangScanAsync(_configuration, barcode);
                AddLog(scanResult.IsSuccess ? $"景旺读码：{scanResult.Message}" : $"景旺读码待处理：{scanResult.Message}");
                await ClearJingWangAlarmAsync("SCANNER_FAILURE", "扫码设备故障已恢复");
            }
            var compatibilityError = ApplyCompatibilityBeforeTest(barcode);
            if (!string.IsNullOrWhiteSpace(compatibilityError))
            {
                if (KeyboardSnMode) ApplySnapshot(_testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration)), false);
                await ShowRuntimePopupAsync(compatibilityError, KeyboardSnMode);
                await RecordScanFailureAsync();
                AddLog(compatibilityError);
                return;
            }

            if (IsJiaLianYiStandardPieceMode
                && (!_configuration.Mes.Enabled || !_configuration.Mes.OnlineMode))
            {
                const string message = "当前测试不是标准片！";
                AddLog($"{message} MES 未在线，无法执行标准片卡关");
                await ShowRuntimePopupAsync(message);
                await RecordScanFailureAsync();
                return;
            }

            if (_configuration.Mes.Enabled && _configuration.Mes.OnlineMode && !ShouldSkipMesPreCheck)
            {
                var session = IsJingWangAccount ? await EnsureJingWangShiftAsync() : null;
                var mesResult = session is { IsSuccess: false } ? session : await _mesRuntimeService.CheckBeforeTestAsync(_configuration, barcode);
                if (IsJingWangAccount) RefreshDerivedState();
                if (IsJingWangAccount && (!mesResult.IsSuccess || mesResult.Code == "SKIPPED"))
                {
                    await RejectJingWangScanAsync(barcode, mesResult.Message);
                    return;
                }
                if (MesRuntimeService.IsJingWang(_configuration))
                    _jingWangPrecheckStatus = mesResult.IsSuccess && mesResult.Code != "SKIPPED" ? "已过站" : "未过站";
                AddLog(mesResult.Code == "SKIPPED" ? $"MES卡关跳过：{mesResult.Message}"
                    : mesResult.IsSuccess ? $"MES卡关通过：{mesResult.Message}" : $"MES卡关失败：{mesResult.Message}");
                if (!string.IsNullOrWhiteSpace(mesResult.Diagnostics))
                {
                    AddLog(mesResult.Diagnostics);
                }
                ApplyJiaLianYiModeFromMesResponse(mesResult, "MES 卡关");
                if (IsJiaLianYiStandardPieceMode && !IsJiaLianYiStandardPiecePreCheckPassed(mesResult))
                {
                    const string message = "当前测试不是标准片！";
                    AddLog($"{message} MES 卡关返回：{mesResult.Message}");
                    await ShowRuntimePopupAsync(message);
                    await RecordScanFailureAsync();
                    return;
                }

                if (!mesResult.IsSuccess)
                {
                    if (MesRuntimeService.IsJingWang(_configuration))
                    {
                        AddLog("景旺过站未通过：本次允许离线测试，测试记录将标记为待人工核对");
                        StatusMessage = "景旺过站未通过，允许离线测试；结果待人工核对";
                    }
                    else
                    {
                    await ShowRuntimePopupAsync($"MES卡关失败：{mesResult.Message}");
                    await RecordScanFailureAsync();
                    return;
                    }
                }
            }
            else if (ShouldSkipMesPreCheck)
            {
                AddLog($"嘉联益{GetJiaLianYiRunModeName(_configuration.User.Jly.RunMode)}：已跳过 MES 卡关");
            }

            if (IsScanAheadMode)
            {
                _scanAheadStatus = ScanAheadStatus.Ok;
                StatusMessage = $"SCANOK：{barcode}，等待下位机 DM";
                AddLog($"SCANOK:{barcode}，等待下位机启动信号 DM");
                return;
            }

            await SendLowerMachineOkAndStartTestAsync(barcode);
        }
        else
        {
            var failureMessage = snapshot.Message;
            if (string.Equals(barcode, "NoRead", StringComparison.OrdinalIgnoreCase)
                && _scannerConnection is CameraQrDeviceConnection camera)
            {
                if (!string.IsNullOrWhiteSpace(camera.LastQualityAdvice))
                    failureMessage += $"\n{camera.LastQualityAdvice}";
                if (!string.IsNullOrWhiteSpace(camera.LastNoReadFramePath))
                    AddLog($"NoRead 原始帧：{camera.LastNoReadFramePath}");
                else if (!string.IsNullOrWhiteSpace(camera.LastNoReadSaveError))
                    AddLog($"NoRead 原始帧保存失败：{camera.LastNoReadSaveError}");
            }
            await ShowRuntimePopupAsync(failureMessage, KeyboardSnMode);
            await RecordScanFailureAsync();
        }
    }

    private void BufferScannerText(string text)
    {
        if (_ignoreScannerDataUntilNextStartSignal)
        {
            AddLog("扫码超时后收到的条码已忽略，等待下位机新的 DM 信号");
            ResetScannerReceiveBuffer();
            return;
        }

        var chunk = NormalizeIncomingText(text);
        if (string.IsNullOrWhiteSpace(chunk))
        {
            return;
        }

        _scannerReceiveBuffer += chunk;
        if (TryExtractScannerBarcode(_scannerReceiveBuffer, out var barcode))
        {
            ResetScannerReceiveBuffer();
            _ = HandleScannerTextAsync(barcode);
            return;
        }

        _scannerReceiveDebounceCts?.Cancel();
        _scannerReceiveDebounceCts?.Dispose();
        _scannerReceiveDebounceCts = new CancellationTokenSource();
        _ = FlushScannerBufferAfterDelayAsync(_scannerReceiveDebounceCts);
    }

    private async Task FlushScannerBufferAfterDelayAsync(CancellationTokenSource debounceCts)
    {
        try
        {
            await Task.Delay(ScannerReceiveDebounceMilliseconds, debounceCts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        PostToUi(() =>
        {
            if (!ReferenceEquals(_scannerReceiveDebounceCts, debounceCts))
            {
                return;
            }

            var barcode = ExtractScannerBarcode(_scannerReceiveBuffer);
            ResetScannerReceiveBuffer();
            if (string.IsNullOrWhiteSpace(barcode))
            {
                AddLog("扫码枪空数据已忽略");
                return;
            }

            _ = HandleScannerTextAsync(barcode);
        });
    }

    private bool TryExtractScannerBarcode(string text, out string barcode)
    {
        barcode = string.Empty;
        text = NormalizeIncomingText(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var separatorIndex = text.IndexOf(';');
        if (separatorIndex >= 0)
        {
            barcode = ExtractScannerBarcode(text[..separatorIndex]);
            return !string.IsNullOrWhiteSpace(barcode);
        }

        if (int.TryParse(_configuration.Identity.BarcodeLength, out var expectedLength)
            && expectedLength > 0
            && text.Length >= expectedLength)
        {
            barcode = text[..expectedLength];
            return true;
        }

        if (string.Equals(text, "NoRead", StringComparison.OrdinalIgnoreCase))
        {
            barcode = text;
            return true;
        }

        return false;
    }

    private string ExtractScannerBarcode(string text)
    {
        text = NormalizeIncomingText(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var separatorIndex = text.IndexOf(';');
        if (separatorIndex >= 0)
        {
            text = text[..separatorIndex];
        }

        if (int.TryParse(_configuration.Identity.BarcodeLength, out var expectedLength)
            && expectedLength > 0
            && text.Length > expectedLength)
        {
            text = text[..expectedLength];
        }

        return text.Trim();
    }

    private void ResetScannerReceiveBuffer()
    {
        _scannerReceiveBuffer = string.Empty;
        _scannerReceiveDebounceCts?.Cancel();
        _scannerReceiveDebounceCts?.Dispose();
        _scannerReceiveDebounceCts = null;
    }

    private void HandleLowerMachineText(string text)
    {
        try
        {
            HandleLowerMachineTextCore(text);
        }
        catch (Exception ex)
        {
            AddDiagnosticLog("上位机 / 接收处理异常", $"下位机数据处理失败：{ex.GetType().Name}：{ex.Message}", "对照通讯日志检查原始报文及当前协议配置，报文异常不等同于硬件损坏。");
        }
    }

    private void HandleLowerMachineTextCore(string text)
    {
        var payload = NormalizeIncomingText(text);
        AddLog($"RecLower:{payload}");
        if (string.IsNullOrWhiteSpace(payload))
        {
            return;
        }

        if (LowerMachineResetSignalPattern.IsMatch(payload))
        {
            DiscardCurrentTestAfterLowerMachineReset();
            if (string.Equals(payload.Trim(), "reset", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        foreach (var completePayload in CollectLowerMachinePayloads(payload))
        {
            ProcessLowerMachinePayload(completePayload);
        }
    }

    private IEnumerable<string> CollectLowerMachinePayloads(string payload)
    {
        if (_discardLowerMachineDataUntilNextStartSignal)
        {
            if (!_protocolAdapter.IsStartSignal(payload))
            {
                ResetLowerMachineReceiveBuffer();
                AddLog("本轮中止后收到的旧周期下位机数据已忽略，等待新的 DM 信号");
                yield break;
            }

            _discardLowerMachineDataUntilNextStartSignal = false;
            _waitingForItemHeader = false;
            _ignoreScannerDataUntilNextStartSignal = false;
            ResetLowerMachineReceiveBuffer();
            AddLog("已收到新的 DM 信号，恢复接收本周期测试结果");
        }

        if (IsStandaloneLowerMachineSignal(payload))
        {
            yield return payload;
            yield break;
        }

        if (!payload.Contains("end", StringComparison.OrdinalIgnoreCase)
            && _protocolAdapter.IsStartSignal(payload))
        {
            ResetLowerMachineReceiveBuffer("下位机启动信号到达，已清空未完成接收缓存");

            yield return payload;
            yield break;
        }

        _lowerMachineReceiveBuffer += payload;
        StartLowerMachinePayloadTimeout();
        while (true)
        {
            var frameStart = (_waitingForItemHeader
                    ? LowerMachineItemFrameStartPattern
                    : LowerMachineFrameStartPattern)
                .Match(_lowerMachineReceiveBuffer);
            if (!frameStart.Success)
            {
                RetainPossibleFramePrefix(_waitingForItemHeader);
                yield break;
            }

            if (frameStart.Index > 0)
            {
                AddLog(_waitingForItemHeader
                    ? "复位后已忽略未完成测试数据，等待新的 Item1 表头"
                    : "已忽略下位机开机自检/非协议报文");
                _lowerMachineReceiveBuffer = _lowerMachineReceiveBuffer[frameStart.Index..];
            }

            // A large Item list can arrive in several serial reads. Stop issuing
            // Itemnum retries once its first fragment is recognized; the complete
            // payload remains subject to end-marker validation below.
            if (_waitingForItemHeader && !_itemHeaderReceptionStarted)
            {
                _itemHeaderReceptionStarted = true;
                CancelTestItemRequestRetry();
                AddLog("已收到 Item1 表头首段，暂停后续表头请求并等待完整 end");
            }

            var endIndex = _lowerMachineReceiveBuffer.IndexOf("end", StringComparison.OrdinalIgnoreCase);
            if (endIndex < 0)
            {
                var maximumCharacters = Math.Max(
                    1,
                    _configuration.Communication.LowerMachineReceiveBufferMaximumCharacters);
                if (_lowerMachineReceiveBuffer.Length > maximumCharacters)
                {
                    _lowerMachineItemHandshakeReply?.TrySetResult(false);
                    _lowerMachineProtocolConfirmed = false;
                    LowerMachineState = "协议异常";
                    AddDiagnosticLog(
                        "下位机 / 报文异常",
                        $"下位机接收缓存超过 {maximumCharacters} 字符，已清空",
                        "检查报文长度、分隔符和结束标记 end。");
                    ResetLowerMachineReceiveBuffer();
                }

                yield break;
            }

            var completePayload = _lowerMachineReceiveBuffer[..(endIndex + 3)];
            _lowerMachineReceiveBuffer = _lowerMachineReceiveBuffer[(endIndex + 3)..];
            CancelLowerMachinePayloadTimeout();
            if (!string.IsNullOrEmpty(_lowerMachineReceiveBuffer))
            {
                StartLowerMachinePayloadTimeout();
            }

            if (!string.IsNullOrWhiteSpace(completePayload))
            {
                yield return completePayload;
            }
        }
    }

    private bool IsStandaloneLowerMachineSignal(string payload)
    {
        return string.IsNullOrEmpty(_lowerMachineReceiveBuffer)
            && !payload.Contains("end", StringComparison.OrdinalIgnoreCase)
            && (_protocolAdapter.IsIgnoredPayload(payload) || _protocolAdapter.IsStartSignal(payload));
    }

    private void ProcessLowerMachinePayload(string payload)
    {
        if (_protocolAdapter.IsIgnoredPayload(payload))
        {
            AddLog("协议适配器忽略下位机数据");
            return;
        }

        if (_protocolAdapter.IsStartSignal(payload))
        {
            if (_lowerMachineItemHandshakeReply is { Task.IsCompleted: false })
            {
                AddLog("连接表头尚未确认，已忽略下位机启动信号");
                return;
            }

            if (IsJingWangAccount && (_jwProcessing || _jwScanning || IsPopupVisible || TestState == "TEST")) { AddLog("当前测试或确认尚未结束，忽略启动信号"); return; }
            if (IsYidong2 && (_jointRun != null || _jointFinishing || _releaseBusy)) { AddLog("联合任务尚未结束，忽略重复 DM"); return; }
            _lowerMachineStartSignalTime = DateTime.Now;
            if (IsScanAheadMode)
            {
                if (_scanAheadStatus == ScanAheadStatus.Ok)
                {
                    AddLog("下位机 DM 到达，检测到 SCANOK，准备发送 DMOK");
                    _ = SendLowerMachineOkAndStartTestAsync(CurrentBarcode);
                }
                else
                {
                    AddLog("下位机 DM 到达，但未检测到 SCANOK，发送 DMNG");
                    _ = SendLowerMachineNgAsync();
                }

                return;
            }

            InitializeFlow();
            if (_configuration.User.NoScanMode)
            {
                AddLog("不扫码模式已启用，跳过扫码枪自动触发");
                _ = StartNoScanAsync();
            }
            else if (IsManualScanMode)
            {
                AddLog("手动扫码模式已启用，等待操作员录入条码或手动触发扫码枪");
            }
            else
            {
                StartScannerWaitTimeout();
                _ = TriggerScannerAsync();
                AddLog("下位机触发扫码，等待扫码枪返回条码");
            }

            return;
        }

        if (_protocolAdapter.IsItemPayload(payload))
        {
            TestFlowSnapshot snapshot;
            try
            {
                snapshot = _testFlowService.LoadTestItems(payload);
            }
            catch (Exception ex)
            {
                RejectItemHeader($"表头解析失败：{ex.GetType().Name}：{ex.Message}");
                return;
            }

            if (!snapshot.Message.StartsWith("测试项目加载完成", StringComparison.Ordinal))
            {
                ApplySnapshot(snapshot, countYield: false);
                RejectItemHeader(snapshot.Message);
                return;
            }

            _waitingForItemHeader = false;
            _itemHeaderReceptionStarted = false;
            CancelTestItemRequestRetry();
            if (_lowerMachineItemHandshakeReply is { } pendingItemReply)
            {
                pendingItemReply.TrySetResult(_lowerMachineItemRequestSent);
            }
            _lowerMachineProtocolConfirmed = _lowerMachineProtocolConfirmed
                || (_configuration.Communication.LowerMachineItemHandshakeEnabled && _lowerMachineItemRequestSent);
            _lowerMachineItemRequestSent = false;
            LowerMachineState = _lowerMachineProtocolConfirmed
                ? _configuration.Communication.LowerMachineHandshakeEnabled || _configuration.Communication.LowerMachineItemHandshakeEnabled
                    ? "握手成功" : "已连接"
                : "已收到表头（未握手）";
            SyncConfiguredTestItems(snapshot);
            ApplySnapshot(snapshot, countYield: false);
            AddLog("收到下位机测试项列表，已按协议适配器解析");
            ContinueTestStartAfterItemHeader();
            return;
        }

        if (_protocolAdapter.IsResultPayload(payload))
        {
            _ = CompleteByPayloadAsync(payload, countYield: true);
            return;
        }

        AddDiagnosticLog("上位机 / 协议识别失败", "完整报文无法识别为启动信号、表头或测试结果", "查看 RecLower 原文，检查协议选择、Item1/Test1 起始标记和 ;end 结束标记。");
    }

    private void RejectItemHeader(string reason)
    {
        _lowerMachineItemHandshakeReply?.TrySetResult(false);
        _lowerMachineProtocolConfirmed = false;
        LowerMachineState = "协议异常";
        _waitingForItemHeader = true;
        _itemHeaderReceptionStarted = false;
        AddDiagnosticLog("上位机 / 表头解析错误", reason, "检查字段数量、分隔符和连续编号；已保留原表头，等待有效新表头。");
    }

    private void ContinueTestStartAfterItemHeader()
    {
        if (!_startTestAfterItemHeader)
        {
            return;
        }

        _startTestAfterItemHeader = false;
        if (IsScanAheadMode)
        {
            if (_scanAheadStatus == ScanAheadStatus.Ok)
            {
                AddLog("复位后 Item1 表头已确认，检测到 SCANOK，准备发送 DMOK");
                _ = SendLowerMachineOkAndStartTestAsync(CurrentBarcode);
            }
            else
            {
                AddLog("复位后 Item1 表头已确认，但未检测到 SCANOK，发送 DMNG");
                _ = SendLowerMachineNgAsync();
            }

            return;
        }

        if (_configuration.User.NoScanMode)
        {
            AddLog("复位后 Item1 表头已确认，不扫码模式开始本次测试");
            _ = StartNoScanAsync();
        }
        else if (IsManualScanMode)
        {
            AddLog("复位后 Item1 表头已确认，等待操作员录入条码或手动触发扫码枪");
        }
        else
        {
            StartScannerWaitTimeout();
            _ = TriggerScannerAsync();
            AddLog("复位后 Item1 表头已确认，触发扫码并等待扫码枪返回条码");
        }
    }

    private async Task TriggerScannerAsync()
    {
        try
        {
            await TriggerScannerCoreAsync();
        }
        catch (Exception ex)
        {
            AddDiagnosticLog("扫码设备 / 触发发送失败", $"扫码触发命令发送失败：{ex.GetType().Name}：{ex.Message}", "检查扫码连接和触发命令；未确认扫码枪成功触发。");
        }
    }

    private async Task TriggerScannerCoreAsync()
    {
        if (_configuration.Communication.Scanner.ConnectionType == ConnectionType.UsbPosHid)
        {
            AddLog("USB POS HID 模式等待操作员按扫码枪按钮，未发送触发命令");
            return;
        }

        if (_configuration.Communication.Scanner.ConnectionType == ConnectionType.CameraQr)
        { 
            if (_scannerConnection is null)
            {
                AddDiagnosticLog("扫码设备 / 相机未连接", "内窥镜未连接，自动识码失败", "检查相机连接及设备选择。");
                return;
            }

            await _scannerConnection.SendStringAsync("scan");
            AddLog("已触发内窥镜抓图识码");
            return;
        }

        if (!_configuration.Communication.ScannerTriggerEnabled)
        {
            AddDiagnosticLog("上位机 / 扫码配置", "通讯配置未启用自动扫码触发，未发送扫码枪触发命令", "自动扫码模式下请启用触发；手动操作应选择手动扫码模式。");
            return;
        }

        if (_scannerConnection is null)
        {
            AddDiagnosticLog("扫码设备 / 未触发扫码", "扫码枪未连接，自动触发失败", "先连接扫码枪，再检查触发命令与通讯设置。");
            return;
        }

        var command = DecodeCommand(_configuration.Communication.ScannerTriggerCommand);
        if (string.IsNullOrEmpty(command))
        {
            AddDiagnosticLog("上位机 / 扫码配置", "扫码触发命令为空，已跳过", "配置扫码枪支持的触发命令和结束符。");
            return;
        }

        await _scannerConnection.SendStringAsync(command);
        AddLog($"sendScanner:{FormatCommandForLog(command)}");
    }

    private async Task StopScannerAsync()
    {
        if (_configuration.Communication.Scanner.ConnectionType is ConnectionType.UsbPosHid or ConnectionType.CameraQr)
        {
            return;
        }

        var command = DecodeCommand(_configuration.Communication.ScannerStopCommand);
        if (string.IsNullOrEmpty(command) || _scannerConnection is null)
        {
            return;
        }

        try
        {
            await _scannerConnection.SendStringAsync(command);
            AddLog($"stopScanner:{FormatCommandForLog(command)}");
        }
        catch (Exception ex)
        {
            AddLog($"扫码停止命令发送失败：{ex.Message}");
        }
    }

    private async Task RequestTestItemsAsync(int attempt, CancellationToken cancellationToken)
    {
        if (_lowerMachineConnection is null
            || !_lowerMachineConnection.IsOpen
            || _configuration.Communication.LowerMachine.ConnectionType == ConnectionType.Disabled)
        {
            AddDiagnosticLog("下位机 / 表头请求失败", $"下位机未连接，第 {attempt}/{TestItemRequestMaxAttempts} 次表头请求已跳过", "检查下位机连接；本次请求尚未发出。");
            return;
        }

        var command = DecodeCommand(_configuration.Communication.TestItemRequestCommand);
        if (string.IsNullOrEmpty(command))
        {
            AddDiagnosticLog("上位机 / 表头配置", "测试项请求命令为空，已跳过", "配置下位机支持的测试项请求命令。");
            return;
        }

        if (await SendLowerMachineAsync(command, cancellationToken))
        {
            _lowerMachineItemRequestSent = true;
            AddLog($"sendLowerCom:{FormatCommandForLog(command)}（表头请求 {attempt}/{TestItemRequestMaxAttempts}）");
        }
    }

    private void StartTestItemRequestRetry(bool waitForAutomaticHeader)
    {
        CancelTestItemRequestRetry();
        if (!_waitingForItemHeader
            || (!_configuration.Communication.TestItemRequestEnabled
                && !_configuration.Communication.LowerMachineItemHandshakeEnabled))
        {
            return;
        }

        var retryCts = new CancellationTokenSource();
        _testItemRequestRetryCts = retryCts;
        _ = RequestTestItemsUntilHeaderReceivedAsync(retryCts, waitForAutomaticHeader);
    }

    private async Task RequestTestItemsUntilHeaderReceivedAsync(
        CancellationTokenSource retryCts,
        bool waitForAutomaticHeader)
    {
        try
        {
            var retryIntervalSeconds = Math.Max(
                1,
                _configuration.Communication.TestItemRequestRetryIntervalSeconds);
            var retryDelay = TimeSpan.FromSeconds(retryIntervalSeconds);
            if (waitForAutomaticHeader)
            {
                await Task.Delay(retryDelay, retryCts.Token);
            }

            for (var attempt = 1; attempt <= TestItemRequestMaxAttempts; attempt++)
            {
                if (!ReferenceEquals(_testItemRequestRetryCts, retryCts) || !_waitingForItemHeader)
                {
                    return;
                }

                try
                {
                    await RequestTestItemsAsync(attempt, retryCts.Token);
                }
                catch (OperationCanceledException) when (retryCts.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AddDiagnosticLog("下位机 / 表头请求失败", $"第 {attempt}/{TestItemRequestMaxAttempts} 次表头请求发送失败：{ex.Message}", "检查通讯连接及请求命令；请区分发送失败和下位机未回复。");
                }

                await Task.Delay(retryDelay, retryCts.Token);
            }

            if (ReferenceEquals(_testItemRequestRetryCts, retryCts) && _waitingForItemHeader)
            {
                if (!_itemHeaderReceptionStarted)
                {
                    _lowerMachineItemHandshakeReply?.TrySetResult(false);
                }
                if (!_lowerMachineProtocolConfirmed && _lowerMachineConnection?.IsOpen == true)
                {
                    LowerMachineState = _lowerMachineItemRequestSent ? "下位机无响应" : "表头请求未发出";
                }
                AddDiagnosticLog("下位机 / 未接收到表头", $"已完成 {TestItemRequestMaxAttempts} 次表头请求尝试（每次等待 {retryIntervalSeconds} 秒），仍未收到 Item1 表头；继续等待表头，不接收残留 Test 数据", "查看发送日志确认请求是否发出，检查下位机响应、通讯线及 Item1...;end 报文格式。");
            }
        }
        catch (OperationCanceledException) when (retryCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _lowerMachineItemHandshakeReply?.TrySetResult(false);
            AddDiagnosticLog("下位机 / 表头请求异常", $"测试项表头请求失败：{ex.Message}", "检查通讯状态及请求配置。");
        }
        finally
        {
            if (ReferenceEquals(_testItemRequestRetryCts, retryCts))
            {
                _testItemRequestRetryCts = null;
            }

            retryCts.Dispose();
        }
    }

    private void CancelTestItemRequestRetry()
    {
        var retryCts = _testItemRequestRetryCts;
        _testItemRequestRetryCts = null;
        retryCts?.Cancel();
    }

    private async Task<bool> SendLowerMachineAsync(string data, CancellationToken cancellationToken = default)
    {
        if (_lowerMachineConnection is null || !_lowerMachineConnection.IsOpen)
        {
            AddDiagnosticLog("下位机 / 命令发送失败", $"下位机未连接，未发送 {FormatCommandForLog(data)}", "检查下位机连接后重新启动流程。");
            return false;
        }

        try
        {
            await _lowerMachineConnection.SendStringAsync(data, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AddDiagnosticLog("下位机 / 命令发送失败", $"发送 {FormatCommandForLog(data)} 失败：{ex.Message}", "检查通讯连接；发送失败不代表下位机已经收到命令。");
            return false;
        }
    }

    private async Task RecordScanFailureAsync()
    {
        if (KeyboardSnMode) return;
        if (IsScanAheadMode)
        {
            _scanAheadStatus = ScanAheadStatus.Ng;
            AddLog("SCANNG：扫码或前置校验失败，等待下位机 DM 后发送 DMNG");
            return;
        }

        await SendLowerMachineNgAsync();
    }

    private async Task SendLowerMachineOkAndStartTestAsync(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            AddLog("条码为空，无法发送 DMOK");
            await SendLowerMachineNgAsync();
            return;
        }

        if (!await EnsureResultFilesAvailableAsync(barcode))
        {
            await SendLowerMachineNgAsync();
            return;
        }

        await StartAcceptedTestAsync(barcode);
    }

    private bool RequiresJointResult => IsYidong2 && _configuration.Export.EnabledPluginIds.Contains("Yidong2",StringComparer.OrdinalIgnoreCase);

    private async Task StartAcceptedTestAsync(string barcode)
    {
        if (IsJingWangAccount && _configuration.Mes.OnlineMode &&
            (_jingWangPrecheckStatus != "已过站" || _jingWangPrecheckBarcode != barcode))
        {
            await RejectJingWangScanAsync(barcode, "缺少本次二维码的过站许可");
            return;
        }
        if (_companionMaintenanceBusy) { AddLog("陪测板正在烧录或写入，请完成后重新起测"); await SendLowerMachineNgAsync(); return; }
        if (IsYidong2 && (_companionConnecting || _companionWorkerTask is { IsCompleted: false }))
        {
            AddLog("陪测板正在连接或退出上一轮 USB 操作，请稍后重新起测");
            await SendLowerMachineNgAsync();
            return;
        }
        var joint = IsYidong2 && _companion.Connected;
        if (IsYidong2 && !joint) CompanionPhase = "Waiting";
        if (!joint && RequiresJointResult)
        {
            StatusMessage = CompanionStatus = "联合测试未启动，请先在陪测板配置页点击 Connect";
            AddLog(StatusMessage);
            await SendLowerMachineNgAsync();
            return;
        }
        if (joint)
        {
            if (_jointRun != null || _jointFinishing || _releaseBusy) return;
            CompanionPhase = "Testing";
            _jointBarcode = barcode;
            _jointRun = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _companionRunCts = new CancellationTokenSource();
            OnPropertyChanged(nameof(CanMaintainCompanion));
        }
        foreach (var command in GetLowerMachineOkCommands(barcode))
        {
            if (!await SendLowerMachineAsync(command))
            {
                _jointRun?.TrySetResult(null);
                _jointRun=null;
                _jointBarcode = null;
                _companionRunCts?.Dispose();
                _companionRunCts = null;
                    OnPropertyChanged(nameof(CanMaintainCompanion));
                return;
            }
            AddLog($"sendLowerCom:{command.Trim()}");
        }
        _lastTestStartedBarcode = barcode;
        _scanAheadStatus = ScanAheadStatus.None;
        ApplySnapshot(_testFlowService.StartTest(), countYield: false);
        if (joint)
        {
            AddLog($"联合起测 SN={barcode}，已启动 USB 陪测");
            _companionWorkerTask = MeasureCompanionAsync(_jointRun!, barcode, _companionRunCts!.Token);
        }
    }

    private async Task SendLowerMachineNgAsync()
    {
        if (await SendLowerMachineAsync(_protocolAdapter.LowerMachineNgCommand))
        {
            AddLog($"sendLowerCom:{_protocolAdapter.LowerMachineNgCommand.Trim()}");
        }
        _scanAheadStatus = ScanAheadStatus.None;
    }

    private string ApplyCompatibilityBeforeTest(string barcode)
    {
        if (!KeyboardSnMode && !UsesBarcodeLbRouting())
        {
            return string.Empty;
        }

        if (!GetBarcodePositionValidationEnabled())
        {
            return string.Empty;
        }

        var position = GetBarcodeValidationPosition();
        if (position <= 0)
        {
            return $"{GetCompatibilityDisplayName()} 条码验证位号配置无效：{position}";
        }

        if (barcode.Length < position)
        {
            return $"{GetCompatibilityDisplayName()} 条码少于 {position} 位：{barcode}";
        }

        var codeValue = barcode[position - 1].ToString().ToUpperInvariant();
        var allowedValues = SplitCompatibilityValues(GetBarcodeValidationAllowedValues());
        if (allowedValues.Count > 0 && !allowedValues.Contains(codeValue, StringComparer.OrdinalIgnoreCase))
        {
            return $"{GetCompatibilityDisplayName()} 条码第 {position} 位必须为 {string.Join("/", allowedValues)}：{barcode}";
        }

        if (!GetBarcodeLimitRuleEnabled())
        {
            return string.Empty;
        }

        var limitItemIndex = GetBarcodeLimitItemIndex();
        var item = _configuration.TestItems.Items
            .OrderBy(configItem => configItem.Order)
            .ElementAtOrDefault(limitItemIndex - 1);
        if (item is null)
        {
            AddLog($"{GetCompatibilityDisplayName()} 第 {limitItemIndex} 项不存在，已跳过条码限值调整");
            return string.Empty;
        }

        item.UpperLimit = GetMappedCompatibilityOption(GetBarcodeLimitUpperByValue(), codeValue, GetCompatibilityOption("LedUpperLimit", "630"));
        item.LowerLimit = GetMappedCompatibilityOption(GetBarcodeLimitLowerByValue(), codeValue, codeValue == "L"
            ? GetCompatibilityOption("LedLLowerLimit", "550")
            : GetCompatibilityOption("LedBLowerLimit", "480"));
        _configurationStore.Save(_configuration);
        ApplySnapshot(_testFlowService.UpdateItemLimits(limitItemIndex, item.LowerLimit, item.UpperLimit), countYield: false);
        AddLog($"{GetCompatibilityDisplayName()} 条码第 {position} 位 {codeValue}，第 {limitItemIndex} 项限值调整为 {item.LowerLimit}~{item.UpperLimit}");
        return string.Empty;
    }

    private IEnumerable<string> GetLowerMachineOkCommands(string barcode)
    {
        if (!UsesBarcodeLbRouting() || !GetBarcodePositionValidationEnabled())
        {
            yield return _protocolAdapter.LowerMachineOkCommand;
            yield break;
        }

        if (GetBarcodeSendDefaultOkCommand())
        {
            yield return _protocolAdapter.LowerMachineOkCommand;
        }

        var position = GetBarcodeValidationPosition();
        if (barcode.Length < position || position <= 0)
        {
            yield break;
        }

        var codeValue = barcode[position - 1].ToString().ToUpperInvariant();
        var command = GetMappedCompatibilityOption(GetBarcodeSuccessCommandByValue(), codeValue, string.Empty);
        if (!string.IsNullOrWhiteSpace(command))
        {
            yield return DecodeCommand(command);
        }
    }

    private bool IsKunshanJiaLianYiIctProfile()
    {
        return string.Equals(_configuration.Compatibility.ProtocolAdapterId, "KunshanJiaLianYi.Ict", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsBominIctProfile()
    {
        return string.Equals(_configuration.Compatibility.ProtocolAdapterId, "Bomin.Ict", StringComparison.OrdinalIgnoreCase);
    }

    private bool UsesBarcodeLbRouting()
    {
        return IsKunshanJiaLianYiIctProfile() || IsBominIctProfile();
    }

    private string GetCompatibilityDisplayName()
    {
        return "兼容模式";
    }

    private string GetCompatibilityOption(string key, string defaultValue)
    {
        return _configuration.Mes.PluginOptions.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    private int GetCompatibilityIntOption(string key, int defaultValue)
    {
        return int.TryParse(GetCompatibilityOption(key, defaultValue.ToString()), out var value)
            ? value
            : defaultValue;
    }

    private bool GetBarcodePositionValidationEnabled()
    {
        return _configuration.Identity.BarcodePositionValidationEnabled
            || IsTrue(GetCompatibilityOption("BarcodePositionValidationEnabled", "false"));
    }

    private int GetBarcodeValidationPosition()
    {
        return _configuration.Identity.BarcodeValidationPosition > 0
            ? _configuration.Identity.BarcodeValidationPosition
            : GetCompatibilityIntOption("BarcodeValidationPosition", 8);
    }

    private string GetBarcodeValidationAllowedValues()
    {
        return !string.IsNullOrWhiteSpace(_configuration.Identity.BarcodeValidationAllowedValues)
            ? _configuration.Identity.BarcodeValidationAllowedValues
            : GetCompatibilityOption("BarcodeValidationAllowedValues", "L,B");
    }

    private bool GetBarcodeLimitRuleEnabled()
    {
        return _configuration.Identity.BarcodeLimitRuleEnabled
            || IsTrue(GetCompatibilityOption("BarcodeLimitRuleEnabled", "false"))
            || _configuration.Mes.PluginOptions.ContainsKey("LimitItemIndex");
    }

    private int GetBarcodeLimitItemIndex()
    {
        return _configuration.Identity.BarcodeLimitItemIndex > 0
            ? _configuration.Identity.BarcodeLimitItemIndex
            : GetCompatibilityIntOption("LimitItemIndex", 12);
    }

    private string GetBarcodeLimitUpperByValue()
    {
        return !string.IsNullOrWhiteSpace(_configuration.Identity.BarcodeLimitUpperByValue)
            ? _configuration.Identity.BarcodeLimitUpperByValue
            : GetCompatibilityOption("LimitUpperByValue", "L=630;B=630");
    }

    private string GetBarcodeLimitLowerByValue()
    {
        return !string.IsNullOrWhiteSpace(_configuration.Identity.BarcodeLimitLowerByValue)
            ? _configuration.Identity.BarcodeLimitLowerByValue
            : GetCompatibilityOption("LimitLowerByValue", "L=550;B=480");
    }

    private bool GetBarcodeSendDefaultOkCommand()
    {
        return _configuration.Communication.BarcodeSendDefaultOkCommand
            || IsTrue(GetCompatibilityOption("BarcodeSendDefaultOkCommand", "false"));
    }

    private string GetBarcodeSuccessCommandByValue()
    {
        return !string.IsNullOrWhiteSpace(_configuration.Communication.BarcodeSuccessCommandByValue)
            ? _configuration.Communication.BarcodeSuccessCommandByValue
            : GetCompatibilityOption("BarcodeSuccessCommandByValue", "L=DMOK1\\r\\n;B=DMOK2\\r\\n");
    }

    private static string GetMappedCompatibilityOption(string mapText, string mapKey, string defaultValue)
    {
        var map = ParseCompatibilityMap(mapText);
        return map.TryGetValue(mapKey, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    private static IReadOnlyList<string> SplitCompatibilityValues(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ',', ';', '|', '/' }, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> ParseCompatibilityMap(string value)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in (value ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = item.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = item[..separator].Trim().ToUpperInvariant();
            var mappedValue = item[(separator + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(key))
            {
                map[key] = mappedValue;
            }
        }

        return map;
    }

    private static bool IsTrue(string value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "y", StringComparison.OrdinalIgnoreCase);
    }

    private void InitializeFlow()
    {
        ResetLowerMachineReceiveBuffer();
        CancelScannerWaitTimeout();
        _ignoreScannerDataUntilNextStartSignal = false;
        _scanAheadStatus = ScanAheadStatus.None;
        var snapshot = _testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration));
        ApplySnapshot(snapshot, countYield: false);
        AddLog(snapshot.Message);
    }

    private void StartScannerWaitTimeout()
    {
        CancelScannerWaitTimeout();
        var timeoutCts = new CancellationTokenSource();
        _scannerWaitTimeoutCts = timeoutCts;
        _ = HandleScannerWaitTimeoutAsync(timeoutCts);
    }

    private async Task HandleScannerWaitTimeoutAsync(CancellationTokenSource timeoutCts)
    {
        try
        {
            var timeoutSeconds = Math.Max(1, _configuration.Communication.ScannerWaitTimeoutSeconds);
            await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), timeoutCts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        PostToUi(() => _ = SendScannerTimeoutNgAsync(timeoutCts));
    }

    private async Task SendScannerTimeoutNgAsync(CancellationTokenSource timeoutCts)
    {
        if (!ReferenceEquals(_scannerWaitTimeoutCts, timeoutCts))
        {
            return;
        }

        _scannerWaitTimeoutCts = null;
        timeoutCts.Dispose();
        _ignoreScannerDataUntilNextStartSignal = true;
        ResetScannerReceiveBuffer();
        AddDiagnosticLog("扫码设备 / 等待扫码超时", $"收到 DM 后等待 {Math.Max(1, _configuration.Communication.ScannerWaitTimeoutSeconds)} 秒，未收到有效扫码返回；可能未触发扫码或扫码枪未响应", "检查触发发送日志、供电、通讯和条码位置。DMOK 是扫码成功后发给下位机的确认，不是扫码枪触发命令。");
        await SendLowerMachineNgAsync();
        ApplySnapshot(_testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration)), countYield: false);
        AddLog($"扫码超时（{Math.Max(1, _configuration.Communication.ScannerWaitTimeoutSeconds)} 秒），已尝试发送 DMNG（结果见发送日志），等待下位机新的 DM 信号");
    }

    private void CancelScannerWaitTimeout()
    {
        _scannerWaitTimeoutCts?.Cancel();
        _scannerWaitTimeoutCts?.Dispose();
        _scannerWaitTimeoutCts = null;
    }

    private static void NormalizeScanMode(UserConfiguration configuration)
    {
        if (configuration.NoScanMode)
        {
            configuration.AutoScan = false;
            configuration.ManualScanMode = false;
            configuration.ScanAheadEnabled = false;
            return;
        }

        if (configuration.ManualScanMode)
        {
            configuration.AutoScan = false;
            return;
        }

        configuration.AutoScan = true;
        configuration.ScanAheadEnabled = false;
    }

    private static bool ApplyKunshanJiaLianYiDedicatedDefaults(AppConfiguration configuration)
    {
        if (!configuration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration)
        {
            return false;
        }

        var changed = !configuration.Mes.Enabled
            || !string.Equals(configuration.Mes.SelectedPlugin, "JiaLianYi.CareerRule", StringComparison.OrdinalIgnoreCase)
            || configuration.Export.EnabledPluginIds.Count != 1
            || !string.Equals(configuration.Export.EnabledPluginIds[0], "JiaLianYi.Export", StringComparison.OrdinalIgnoreCase)
            || !configuration.Communication.AutoConnectScannerOnStartup
            || !configuration.Communication.AutoConnectLowerMachineOnStartup
            || !configuration.User.SaveDailyQuantity;
        configuration.Mes.Enabled = true;
        configuration.Mes.SelectedPlugin = "JiaLianYi.CareerRule";
        configuration.Export.EnabledPluginIds = new List<string> { "JiaLianYi.Export" };
        // 昆山嘉联益现场上位机启动后应直接连接扫码枪和下位机。
        configuration.Communication.AutoConnectScannerOnStartup = true;
        configuration.Communication.AutoConnectLowerMachineOnStartup = true;
        // 嘉联益良率跨日期、跨重启持续累计，只由“清空良率”按钮归零。
        configuration.User.SaveDailyQuantity = true;
        return changed;
    }

    private static bool ApplyJiaLianYiStartupProductionMode(AppConfiguration configuration)
    {
        if (!configuration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration
            || configuration.User.Jly.RunMode == JiaLianYiRunMode.TestPiece)
        {
            return false;
        }

        configuration.User.Jly.RunMode = JiaLianYiRunMode.TestPiece;
        return true;
    }

    private void ResetLowerMachineReceiveBuffer(string? message = null)
    {
        if (!string.IsNullOrEmpty(_lowerMachineReceiveBuffer) && !string.IsNullOrWhiteSpace(message))
        {
            AddLog(message);
        }

        _lowerMachineReceiveBuffer = string.Empty;
        _itemHeaderReceptionStarted = false;
        CancelLowerMachinePayloadTimeout();
    }

    private void DiscardCurrentTestAfterLowerMachineReset()
    {
        _lowerMachineItemHandshakeReply?.TrySetResult(false);
        _lowerMachineProtocolConfirmed = !_configuration.Communication.LowerMachineItemHandshakeEnabled;
        _lowerMachineItemRequestSent = false;
        LowerMachineState = _lowerMachineProtocolConfirmed
            ? _configuration.Communication.LowerMachineHandshakeEnabled ? "握手成功" : "已连接"
            : "待表头确认";
        ResetLowerMachineReceiveBuffer();
        CancelScannerWaitTimeout();
        _ignoreScannerDataUntilNextStartSignal = true;
        _waitingForItemHeader = true;
        _startTestAfterItemHeader = false;
        _discardLowerMachineDataUntilNextStartSignal = false;
        _scanAheadStatus = ScanAheadStatus.None;
        _pendingJlyRetestFailure = null;
        _lowerMachineStartSignalTime = null;

        var snapshot = _testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration));
        ApplySnapshot(snapshot, countYield: false);
        AddDiagnosticLog("下位机 / 复位或急停", "收到下位机 reset：当前测试数据已作废；正在立即请求 Item1 表头。下一次 DM 仅用于启动新一轮测试。", "检查急停和复位状态；确认下位机可响应 Itemnum 并返回 Item1...;end。");
        StartTestItemRequestRetry(waitForAutomaticHeader: false);
    }

    private void StartLowerMachinePayloadTimeout()
    {
        if (string.IsNullOrEmpty(_lowerMachineReceiveBuffer))
        {
            return;
        }

        if (_lowerMachinePayloadTimeoutCts is not null)
        {
            return;
        }

        var timeoutCts = new CancellationTokenSource();
        _lowerMachinePayloadTimeoutCts = timeoutCts;
        _ = DiscardLowerMachinePayloadAfterTimeoutAsync(timeoutCts);
    }

    private async Task DiscardLowerMachinePayloadAfterTimeoutAsync(CancellationTokenSource timeoutCts)
    {
        try
        {
            var timeoutSeconds = Math.Max(1, _configuration.Communication.LowerMachinePayloadTimeoutSeconds);
            await Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), timeoutCts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        PostToUi(() =>
        {
            if (!ReferenceEquals(_lowerMachinePayloadTimeoutCts, timeoutCts)
                || string.IsNullOrEmpty(_lowerMachineReceiveBuffer))
            {
                return;
            }

            ResetLowerMachineReceiveBuffer();
            _lowerMachineItemHandshakeReply?.TrySetResult(false);
            _lowerMachineProtocolConfirmed = false;
            _lowerMachineItemRequestSent = false;
            LowerMachineState = "报文超时";
            _waitingForItemHeader = true;
            _startTestAfterItemHeader = false;
            _discardLowerMachineDataUntilNextStartSignal = false;
            _scanAheadStatus = ScanAheadStatus.None;
            _pendingJlyRetestFailure = null;
            _lowerMachineStartSignalTime = null;
            ApplySnapshot(_testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration)), countYield: false);
            AddDiagnosticLog("下位机 / 报文接收超时", $"下位机报文超过 {_configuration.Communication.LowerMachinePayloadTimeoutSeconds} 秒未收到 end 或 reset：当前测试数据已作废；等待新的 Item1 表头，缺失时最多请求 3 次", "表头或结果仅收到部分数据。检查结束标记、分包间隔和通讯连接。");
            StartTestItemRequestRetry(waitForAutomaticHeader: true);
        });
    }

    private void CancelLowerMachinePayloadTimeout()
    {
        _lowerMachinePayloadTimeoutCts?.Cancel();
        _lowerMachinePayloadTimeoutCts?.Dispose();
        _lowerMachinePayloadTimeoutCts = null;
    }

    private void RetainPossibleFramePrefix(bool itemHeaderOnly = false)
    {
        var possiblePrefixes = itemHeaderOnly
            ? new[] { "Item1,", "Item1", "Item", "Ite", "It", "I" }
            : new[] { "Item1,", "Item1", "Item", "Ite", "It", "I", "Test1,", "Test1", "Test", "Tes", "Te", "T" };
        var retained = possiblePrefixes
            .Where(prefix => _lowerMachineReceiveBuffer.EndsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(prefix => prefix.Length)
            .FirstOrDefault();

        if (retained is null)
        {
            if (!string.IsNullOrWhiteSpace(_lowerMachineReceiveBuffer))
            {
                AddLog("已忽略下位机开机自检/非协议报文");
            }

            ResetLowerMachineReceiveBuffer();
            return;
        }

        _lowerMachineReceiveBuffer = retained;
    }

    private void SimulateScan()
    {
        _ = HandleScannerTextAsync(BuildSampleBarcode());
    }

    private void SimulateLowerMachineStart()
    {
        AddLog("模拟下位机启动信号：DM");
        HandleLowerMachineText("DM");
    }

    private async Task StartNoScanAsync()
    {
        var barcode = "NOSCAN" + DateTime.Now.ToString("yyyyMMddHHmmss");
        AddLog($"不扫码模式：{barcode}");
        if (!await EnsureResultFilesAvailableAsync(barcode))
        {
            await SendLowerMachineNgAsync();
            return;
        }

        var snapshot = _testFlowService.AcceptBarcode(barcode, TestFlowOptions.ForNoScan());
        ApplySnapshot(snapshot, countYield: false);

        await StartAcceptedTestAsync(barcode);
    }

    private async Task CompleteByPayloadAsync(string payload, bool countYield)
    {
        if (!IsJingWangAccount) { await CompletePayloadCoreAsync(payload, countYield); return; }
        if (_jwProcessing || _testFlowService.Snapshot().State != TestFlowState.Testing)
        {
            AddLog("忽略未获准起测的设备结果");
            return;
        }
        _jwProcessing = true;
        OnPropertyChanged(nameof(CanEditJingWangFields));
        try { await CompletePayloadCoreAsync(payload, countYield); }
        finally { _jwProcessing = false; OnPropertyChanged(nameof(CanEditJingWangFields)); }
    }

    private async Task CompletePayloadCoreAsync(string payload, bool countYield)
    {
        if (IsYidong2 && (_releaseBusy || _jointFinishing)) return;
        if (RequiresJointResult && _jointRun == null) { AddLog("本轮未启动陪测，忽略单侧结果，未导出 CSV"); return; }
        var fallbackStartTime = IsKunshanJiaLianYiDedicatedConfiguration
            ? _lowerMachineStartSignalTime
            : null;
        TestFlowSnapshot snapshot;
        try
        {
            snapshot = _testFlowService.CompleteTest(payload, fallbackStartTime);
        }
        catch (Exception ex)
        {
            _lowerMachineProtocolConfirmed = false;
            LowerMachineState = "协议异常";
            AddDiagnosticLog("上位机 / 结果解析错误", $"测试结果解析失败：{ex.GetType().Name}：{ex.Message}", "检查原始结果报文、字段和编号；本报文未计入产量、未保存为测试结果、未上传 MES。");
            return;
        }
        _lowerMachineStartSignalTime = null;
        if (IsYidong2 && _jointRun != null)
        {
            var jointRun = _jointRun;
            if (snapshot.Barcode != _jointBarcode) { AddLog("本机结果 SN 与陪测任务不一致"); return; }
            // 主界面的明细、判定和良率只反映下位机，本机完成后立即更新。
            ApplySnapshot(snapshot, countYield);
            PersistDailyYieldStatistics();
            _jointFinishing=true;
            try
            {
                var rows=await jointRun.Task;
                if (!ReferenceEquals(_jointRun, jointRun)) return;
                if(rows==null) { AbortJointRun("陪测异常，未提交联合结果"); return; }
                int next=snapshot.Items.Select(i=>i.Index).DefaultIfEmpty().Max();
                var extra=rows.Select(r=>new TestItemResult {Index=++next,ItemName=r.ItemName,UpperLimit=r.UpperLimit,
                    LowerLimit=r.LowerLimit,TestValue=r.TestValue,Result=r.Result}).ToArray();
                var failed=extra.FirstOrDefault(r=>!r.IsPass);
                var merged=new TestFlowSnapshot { Barcode=snapshot.Barcode,
                    State=snapshot.IsPass==true && failed==null ? TestFlowState.Completed : TestFlowState.Failed, StartTime=snapshot.StartTime,
                    EndTime=DateTime.Now, Items=snapshot.Items.Concat(extra).ToArray(), IsPass=snapshot.IsPass==true && failed==null,
                    FirstFailItem=snapshot.IsPass==true?failed?.ItemName??"":snapshot.FirstFailItem,
                    FirstFailValue=snapshot.IsPass==true?failed?.TestValue??"":snapshot.FirstFailValue,Message="联合测试完成" };
                var ctx=new ResultExportContext {Configuration=_configuration,Snapshot=merged,PluginOptions=_configuration.Export.GetOptions("Yidong2")};
                var exporter=new Yidong2Exporter();
                var exports=await ExportResultAsync(merged);
                var error=exports.FirstOrDefault(item=>!item.IsSuccess);
                if (error != null) throw new IOException(error.Message);
                if (!RequiresJointResult)
                {
                    var csv=await exporter.ExportAsync(ctx);
                    if (!csv.IsSuccess) throw new IOException(csv.Message);
                }
                var piece=await exporter.ExportPieceAsync(ctx);
                if (!piece.IsSuccess) throw new IOException(piece.Message);
                AddLog($"联合 CSV：{piece.FilePath}");
                RecordProductionResult(merged);
                PersistDailyYieldStatistics();
                await UpdateConsecutiveNgAlarmAsync(merged);
                if (_configuration.Mes.Enabled && _configuration.Mes.OnlineMode)
                {
                    var result=await _mesRuntimeService.UploadResultAsync(_configuration,merged,[new MesUploadFile {FilePath=piece.FilePath}]);
                    AddLog(result.Message);
                    if (!result.IsSuccess) return;
                }
                if (await SendLowerMachineAsync(_protocolAdapter.TestOverCommand))
                {
                    AddLog($"联合测试完成，已发送 TestOver：{merged.Barcode}");
                    _jointRun=null;
                    OnPropertyChanged(nameof(CanMaintainCompanion));
                    _jointBarcode=null;
                    CurrentBarcode=string.Empty;
                }
            }
            catch(Exception ex) { AddLog($"联合结果处理失败：{ex.Message}"); }
            finally
            {
                if (_jointRun is null || ReferenceEquals(_jointRun, jointRun)) _jointFinishing = false;
                OnPropertyChanged(nameof(CanMaintainCompanion));
            }
            return;
        }
        if (IsYidong2) CompanionPhase = "Waiting";
        ApplySnapshot(snapshot, countYield);
        AddLog(snapshot.Message);

        if (snapshot.IsPass.HasValue)
        {
            if (UseJingWangRecords)
            {
                _jingWangLatestCraftSnapshot = snapshot;
                await ClearJingWangAlarmAsync("LOWER_MACHINE_FAILURE", "下位机通讯或协议已恢复");
                await ClearJingWangAlarmAsync("HOST_PROTOCOL_FAILURE", "上位机测试流程已恢复");
            }
            if (!await EnsureResultFilesAvailableAsync(snapshot.Barcode))
            {
                return;
            }

            var holdJiaLianYiMesUpload = ShouldHoldJiaLianYiMesUpload(snapshot);
            if (holdJiaLianYiMesUpload)
            {
                BeginJiaLianYiMesUpload(snapshot.Barcode);
            }

            try
            {
                RecordProductionResult(snapshot);
                PersistDailyYieldStatistics();
                await UpdateConsecutiveNgAlarmAsync(snapshot);
                var exportResults = await ExportResultAsync(snapshot);
                if (IsKunshanJiaLianYiDedicatedConfiguration && snapshot.IsPass == false && _configuration.User.Jly.ShowFailResultPopup)
                {
                    await ShowRuntimePopupAsync("测试结果：FAIL", requiresConfirmation: false);
                }

                if (IsYidong2 && _configuration.Export.EnabledPluginIds.Contains("Yidong2", StringComparer.OrdinalIgnoreCase))
                    AddLog("本机结果已处理，Yidong2 联合 MES 上传等待陪测结果");
                else
                    await UploadMesResultAsync(snapshot, exportResults);
            }
            finally
            {
                if (holdJiaLianYiMesUpload)
                {
                    EndJiaLianYiMesUpload(snapshot.Barcode);
                }
            }
        }

        // The completed snapshot has already been recorded, exported and uploaded; clear the display for the next unit.
        CurrentBarcode = string.Empty;
    }

    private async Task<IReadOnlyList<ResultExportResult>> ExportResultAsync(TestFlowSnapshot snapshot)
    {
        var results = await _exportRuntimeService.ExportAsync(_configuration, snapshot, jingWang: IsJingWangAccount);

        foreach (var result in results)
        {
            AddLog(result.IsSuccess
                ? $"{result.ExporterName}:{result.Message} {result.FilePath}"
                : $"{result.ExporterName}:{result.Message}");
        }

        return results;
    }

    private static IReadOnlyList<MesUploadFile> GetMesCsvFiles(
        IReadOnlyList<ResultExportResult>? exportResults,
        AppConfiguration configuration,
        TestFlowSnapshot snapshot,
        out string diagnostics)
    {
        var exportDetails = exportResults is null
            ? "本次没有收到导出器执行结果。"
            : string.Join("；", exportResults.Select(result =>
                $"{result.ExporterName}[成功={result.IsSuccess}，路径={FormatLogPath(result.FilePath)}，存在={IsAnyCsvPathPresent(result.FilePath)}]"));

        var files = exportResults?
            .Where(result => result.IsSuccess && !string.IsNullOrWhiteSpace(result.FilePath))
            .SelectMany(result => result.FilePath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(path => path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new MesUploadFile { FilePath = path })
            .ToList()
            ?? [];

        if (files.Count > 0)
        {
            diagnostics = $"MES CSV 附件：使用导出器返回路径。{exportDetails}";
            return files;
        }

        var fallbackFiles = FindCurrentTestCsvFiles(configuration, snapshot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new MesUploadFile { FilePath = path })
            .ToList();
        diagnostics = fallbackFiles.Count > 0
            ? $"MES CSV 附件：导出器未返回可用 CSV，已从当前测试时间段的输出目录回退找到：{string.Join("；", fallbackFiles.Select(file => file.FilePath))}。{exportDetails}"
            : $"MES CSV 附件：未找到可用 CSV。{exportDetails}";
        return fallbackFiles;
    }

    private static IEnumerable<string> FindCurrentTestCsvFiles(AppConfiguration configuration, TestFlowSnapshot snapshot)
    {
        var outputDirectory = configuration.Output.CsvDirectory;
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            return Array.Empty<string>();
        }

        var directory = configuration.Output.ClassifyCsvByResult
            ? Path.Combine(outputDirectory, snapshot.IsPass == true ? "PASS" : "FAIL")
            : outputDirectory;
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var startedAt = snapshot.StartTime ?? snapshot.EndTime ?? DateTime.Now;
        var earliestWriteTime = startedAt.AddSeconds(-10);
        try
        {
            return Directory.EnumerateFiles(directory, "*.csv", SearchOption.TopDirectoryOnly)
                .Where(path => File.GetLastWriteTime(path) >= earliestWriteTime && File.GetLastWriteTime(path) <= DateTime.Now.AddMinutes(1))
                .OrderByDescending(File.GetLastWriteTime)
                .Take(1)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsAnyCsvPathPresent(string filePath) =>
        filePath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(path => path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && File.Exists(path));

    private static string FormatLogPath(string filePath) =>
        string.IsNullOrWhiteSpace(filePath) ? "无" : filePath;

    private async Task<bool> EnsureResultFilesAvailableAsync(string barcode)
    {
        if (IsJingWangAccount && (string.IsNullOrWhiteSpace(JwProduct) || string.IsNullOrWhiteSpace(JwSpec)
            || string.IsNullOrWhiteSpace(JwSummaryPath) || string.IsNullOrWhiteSpace(JwPiecePath)))
        {
            _jingWangPrecheckStatus = "未过站";
            ApplySnapshot(_testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration)), false);
            await ShowRuntimePopupAsync("请在主界面填写料号、工序代码和两处 CSV 保存目录", true, true);
            return false;
        }
        var lockedFiles = GetLockedResultFiles(barcode);
        if (lockedFiles.Count == 0)
        {
            return true;
        }

        var message = $"以下 CSV 档案正在被占用或不可写入，本次测试结果不会保存、也不会上传 MES。请关闭文件后重新测试：{Environment.NewLine}"
            + string.Join(Environment.NewLine, lockedFiles);
        AddLog(message.Replace(Environment.NewLine, "；"));
        await ShowRuntimePopupAsync(message, force: true, requiresConfirmation: true);
        return false;
    }

    private IReadOnlyList<string> GetLockedResultFiles(string barcode)
    {
        var enabledExporters = _configuration.Export.EnabledPluginIds
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidateFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var output = _configuration.Output;

        if (enabledExporters.Contains("Default.Csv"))
        {
            var fileName = IsJingWangAccount ? CsvResultExporter.JingWangFileName(_configuration, DateTime.Now) : $"{output.CsvName}_{DateTime.Now:yyyyMMdd}.csv";
            if (output.ClassifyCsvByResult)
            {
                candidateFiles.Add(Path.Combine(output.CsvDirectory, "PASS", fileName));
                candidateFiles.Add(Path.Combine(output.CsvDirectory, "FAIL", fileName));
            }
            else
            {
                candidateFiles.Add(Path.Combine(output.CsvDirectory, fileName));
            }
        }

        if (enabledExporters.Contains("Default.CsvMes"))
        {
            candidateFiles.Add(Path.Combine(output.DclDirectory, $"{DateTime.Now:yyyyMMdd}.csv"));
        }

        if (enabledExporters.Contains("Default.Dcl") && !string.IsNullOrWhiteSpace(barcode))
        {
            candidateFiles.Add(Path.Combine(output.DclDirectory, $"{barcode}.dcl"));
        }

        AddJiaLianYiCsvCandidates(enabledExporters, candidateFiles, barcode);

        return candidateFiles
            .Where(File.Exists)
            .Where(IsFileLockedForWrite)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void AddJiaLianYiCsvCandidates(
        ISet<string> enabledExporters,
        ISet<string> candidateFiles,
        string barcode)
    {
        if (!enabledExporters.Contains("JiaLianYi.Export"))
        {
            return;
        }

        var options = _configuration.Export.GetOptions("JiaLianYi.Export");
        var csvName = GetExportOption(options, "CsvName", _configuration.Output.CsvName);
        var fileName = ResultExportFileName.ApplyTestPieceSuffix(
            _configuration,
            $"{csvName}_{DateTime.Now:yyyyMMdd}.csv");
        var passDirectory = GetExportOption(options, "CsvDirectory", _configuration.Output.CsvDirectory);
        var failDirectory = GetExportOption(options, "FailCsvDirectory", passDirectory);

        candidateFiles.Add(Path.Combine(GetJiaLianYiCsvDirectory(options, passDirectory, barcode), fileName));
        candidateFiles.Add(Path.Combine(GetJiaLianYiCsvDirectory(options, failDirectory, barcode), fileName));
    }

    private static string GetJiaLianYiCsvDirectory(
        IReadOnlyDictionary<string, string> options,
        string baseDirectory,
        string barcode)
    {
        if (!IsExportOptionEnabled(options, "LegacyDirectoryLayout"))
        {
            return baseDirectory;
        }

        var flag = barcode.Length >= 9 ? barcode[8].ToString() : "_";
        return Path.Combine(baseDirectory, flag, "CSV");
    }

    private static string GetExportOption(IReadOnlyDictionary<string, string> options, string key, string defaultValue)
    {
        return options.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
    }

    private static bool IsExportOptionEnabled(IReadOnlyDictionary<string, string> options, string key)
    {
        return options.TryGetValue(key, out var value) && bool.TryParse(value, out var enabled) && enabled;
    }

    private static bool IsFileLockedForWrite(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private async Task UploadMesResultAsync(TestFlowSnapshot snapshot, IReadOnlyList<ResultExportResult>? exportResults = null)
    {
        if (IsJingWangAccount && !_configuration.Mes.OnlineMode)
        {
            if (snapshot.IsPass == false) await ShowRuntimePopupAsync($"SN {snapshot.Barcode} 离线测试 FAIL：{snapshot.FirstFailItem}", true, true);
            return;
        }
        if (IsJiaLianYiDebugMode)
        {
            AddLog("嘉联益调试模式：已跳过 MES 上传");
            return;
        }

        if (!_configuration.Mes.Enabled || (!_configuration.Mes.OnlineMode && !MesRuntimeService.IsJingWang(_configuration)))
        {
            return;
        }

        if (IsKunshanJiaLianYiDedicatedConfiguration && _configuration.User.Jly.RecordSecondTestYield)
        {
            if (_pendingJlyRetestFailure is not null)
            {
                if (string.Equals(_pendingJlyRetestFailure.Barcode, snapshot.Barcode, StringComparison.OrdinalIgnoreCase))
                {
                    AddLog($"JLY 二次测试完成：SN {snapshot.Barcode}，按第二次结果上传 MES");
                    _pendingJlyRetestFailure = null;
                }
                else
                {
                    AddLog($"JLY 二次测试未完成：SN {_pendingJlyRetestFailure.Barcode} 已按首次 FAIL 上传 MES");
                    await UploadMesSnapshotAsync(_pendingJlyRetestFailure, exportResults);
                    _pendingJlyRetestFailure = null;
                }
            }

            if (snapshot.IsPass == false)
            {
                _pendingJlyRetestFailure = snapshot;
                AddLog($"JLY 已记录首次 FAIL：SN {snapshot.Barcode}，等待同一 SN 的第二次测试结果后上传 MES");
                return;
            }
        }

        await UploadMesSnapshotAsync(snapshot, exportResults);
    }

    private async Task UploadMesSnapshotAsync(TestFlowSnapshot snapshot, IReadOnlyList<ResultExportResult>? exportResults = null)
    {
        if (ShouldBlockMesUploadByYield(snapshot))
        {
            AddLog($"JLY 生产模式 FAIL 的当前良率 {YieldRate:F2}% 小于或等于设定值 {_configuration.User.Jly.YieldUploadThreshold:F2}%，不上传 MES");
            return;
        }

        MesResult result;
        if (IsJiaLianYiProductionMode)
        {
            await _jiaLianYiMesUploadGate.WaitAsync();
            try
            {
                result = await UploadJiaLianYiMesResultWithTimeoutAsync(snapshot);
            }
            finally
            {
                _jiaLianYiMesUploadGate.Release();
            }
        }
        else
        {
            IReadOnlyList<MesUploadFile> mesCsvFiles;
            string csvDiagnostics;
            if (MesRuntimeService.IsJingWang(_configuration))
            {
                mesCsvFiles = exportResults?
                    .Where(item => item.IsSuccess && item.ExporterName == "单片 CSV" && File.Exists(item.FilePath))
                    .Select(item => new MesUploadFile { FilePath = item.FilePath })
                    .ToArray() ?? [];
                csvDiagnostics = mesCsvFiles.Count == 0 ? "景旺单片 CSV 未生成" : $"景旺单片 CSV：{mesCsvFiles[0].FilePath}";
            }
            else
                mesCsvFiles = GetMesCsvFiles(exportResults, _configuration, snapshot, out csvDiagnostics);
            AddLog(csvDiagnostics);
            result = await _mesRuntimeService.UploadResultAsync(_configuration, snapshot, mesCsvFiles,
                precheckStatus: MesRuntimeService.IsJingWang(_configuration) && _jingWangPrecheckBarcode == snapshot.Barcode
                    ? _jingWangPrecheckStatus : "未过站");
        }

        if (string.Equals(result.Code, "DUPLICATE", StringComparison.OrdinalIgnoreCase))
        {
            AddLog($"MES上传跳过：{result.Message}");
            return;
        }

        var message = result.IsSuccess ? $"MES上传成功：{result.Message}" : $"MES上传失败：{result.Message}";
        AddLog(message);
        ApplyJiaLianYiModeFromMesResponse(result, "MES 上传");
        if (IsJingWangAccount && (!result.IsSuccess || (_configuration.Mes.OnlineMode && result.Code != "JINGWANG_OK") || snapshot.IsPass == false))
        {
            var heading = result.Code == "JINGWANG_RETEST" ? "MES 要求重测" : "测试结果提醒";
            await ShowRuntimePopupAsync($"{heading}　SN {snapshot.Barcode}　设备结果 {(snapshot.IsPass == true ? "PASS" : "FAIL")}\n{message}", true, true);
            return;
        }
        if (string.Equals(_configuration.Mes.SelectedPlugin, "HongXin.OA", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(result.Diagnostics))
        {
            AddLog($"宏信上传明细 SN={snapshot.Barcode}\n{result.Diagnostics}");
        }
        if (!result.IsSuccess)
        {
            if (!string.Equals(_configuration.Mes.SelectedPlugin, "HongXin.OA", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(result.Diagnostics))
            {
                AddLog(result.Diagnostics);
            }

            _ = ShowRuntimePopupAsync(message, requiresConfirmation: false);
        }
    }

    private async Task<MesResult> UploadJiaLianYiMesResultWithTimeoutAsync(TestFlowSnapshot snapshot)
    {
        var uploadTask = _mesRuntimeService.UploadResultAsync(_configuration, snapshot);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(JiaLianYiMesUploadTimeoutSeconds));
        if (await Task.WhenAny(uploadTask, timeoutTask) == uploadTask)
        {
            return await uploadTask;
        }

        _ = ObserveLateJiaLianYiMesUploadAsync(uploadTask, snapshot.Barcode);
        return MesResult.Fail($"MES 上传超时：{JiaLianYiMesUploadTimeoutSeconds} 秒未收到反馈");
    }

    private async Task ObserveLateJiaLianYiMesUploadAsync(Task<MesResult> uploadTask, string barcode)
    {
        try
        {
            var lateResult = await uploadTask;
            AddLog($"MES 上传超时后的延迟返回已忽略：SN {barcode}，{lateResult.Message}");
        }
        catch (Exception ex)
        {
            AddLog($"MES 上传超时后的延迟任务异常已忽略：SN {barcode}，{ex.Message}");
        }
    }

    private Task SimulatePopupAsync()
    {
        return ShowRuntimePopupAsync("模拟提醒：MES 上传失败，请检查网络连接或 MES 服务状态。", force: true);
    }

    private bool ShouldBlockMesUploadByYield(TestFlowSnapshot snapshot)
    {
        if (!IsKunshanJiaLianYiDedicatedConfiguration || !_configuration.User.Jly.TestPieceMode || snapshot.IsPass == true)
        {
            return false;
        }

        return YieldRate <= _configuration.User.Jly.YieldUploadThreshold;
    }

    private bool IsJiaLianYiDebugMode => IsKunshanJiaLianYiDedicatedConfiguration
        && _configuration.User.Jly.RunMode == JiaLianYiRunMode.Debug;

    private bool IsJiaLianYiProductionMode => IsKunshanJiaLianYiDedicatedConfiguration
        && _configuration.User.Jly.RunMode == JiaLianYiRunMode.TestPiece;

    private bool ShouldHoldJiaLianYiMesUpload(TestFlowSnapshot snapshot)
    {
        return snapshot.IsPass.HasValue
            && IsJiaLianYiProductionMode
            && _configuration.Mes.Enabled
            && _configuration.Mes.OnlineMode;
    }

    private async Task WaitForJiaLianYiMesUploadAsync()
    {
        if (!IsJiaLianYiProductionMode)
        {
            return;
        }

        Task? completionTask;
        lock (_jiaLianYiMesUploadSync)
        {
            completionTask = _pendingJiaLianYiMesUploadCount > 0
                ? _jiaLianYiMesUploadCompletion?.Task
                : null;
        }

        if (completionTask is null)
        {
            return;
        }

        AddLog("嘉联益生产模式：正在等待上一笔 MES 上传返回，暂不启动新的测试");
        await completionTask;
    }

    private void BeginJiaLianYiMesUpload(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        lock (_jiaLianYiMesUploadSync)
        {
            if (_pendingJiaLianYiMesUploadCount == 0)
            {
                _jiaLianYiMesUploadCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            _pendingJiaLianYiMesUploadCount++;
        }
    }

    private void EndJiaLianYiMesUpload(string barcode)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return;
        }

        TaskCompletionSource? completion = null;
        lock (_jiaLianYiMesUploadSync)
        {
            _pendingJiaLianYiMesUploadCount = Math.Max(0, _pendingJiaLianYiMesUploadCount - 1);
            if (_pendingJiaLianYiMesUploadCount == 0)
            {
                completion = _jiaLianYiMesUploadCompletion;
                _jiaLianYiMesUploadCompletion = null;
            }
        }

        completion?.TrySetResult();
    }

    private bool IsJiaLianYiStandardPieceMode => IsKunshanJiaLianYiDedicatedConfiguration
        && _configuration.User.Jly.RunMode == JiaLianYiRunMode.StandardPiece;

    private bool ShouldSkipMesPreCheck => IsKunshanJiaLianYiDedicatedConfiguration
        && _configuration.User.Jly.RunMode == JiaLianYiRunMode.Debug;

    private static bool IsJiaLianYiStandardPiecePreCheckPassed(MesResult result)
    {
        if (!result.IsSuccess)
        {
            return false;
        }

        var response = string.Join("\n", new[] { result.Message, result.RawResponse, result.Diagnostics });
        var normalizedResponse = System.Text.RegularExpressions.Regex.Replace(response, @"\s+", string.Empty)
            .Replace('：', ':');
        return normalizedResponse.Contains("MES卡关通过:標準片", StringComparison.OrdinalIgnoreCase)
            || normalizedResponse.Contains("MES卡关通过:标准片", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyJiaLianYiModeFromMesResponse(MesResult result, string source)
    {
        if (!IsKunshanJiaLianYiDedicatedConfiguration)
        {
            return;
        }

        var response = string.Join("\n", new[] { result.Message, result.RawResponse, result.Diagnostics });
        if (response.Contains("標準片驗證未確實", StringComparison.OrdinalIgnoreCase)
            || response.Contains("标准片验证未确实", StringComparison.OrdinalIgnoreCase))
        {
            SwitchJiaLianYiRunModeFromMes(JiaLianYiRunMode.StandardPiece, $"{source} 返回标准片验证未确实");
            return;
        }

        var normalizedResponse = System.Text.RegularExpressions.Regex.Replace(response, @"\s+", string.Empty)
            .Replace('：', ':');
        if (result.IsSuccess
            && (normalizedResponse.Contains("MES卡关通过:標準片", StringComparison.OrdinalIgnoreCase)
                || normalizedResponse.Contains("MES卡关通过:标准片", StringComparison.OrdinalIgnoreCase)))
        {
            SwitchJiaLianYiRunModeFromMes(JiaLianYiRunMode.StandardPiece, $"{source} 返回标准片");
            return;
        }

        if (result.IsSuccess
            && _configuration.User.Jly.RunMode == JiaLianYiRunMode.StandardPiece
            && response.Contains("Golden sample testing:Y", StringComparison.OrdinalIgnoreCase)
            && response.Contains("Tooling status:Y", StringComparison.OrdinalIgnoreCase))
        {
            SwitchJiaLianYiRunModeFromMes(JiaLianYiRunMode.TestPiece, $"{source} 返回 Golden sample testing:Y 且 Tooling status:Y");
        }
    }

    private void SwitchJiaLianYiRunModeFromMes(JiaLianYiRunMode mode, string reason)
    {
        if (_configuration.User.Jly.RunMode == mode)
        {
            return;
        }

        _configuration.User.Jly.RunMode = mode;
        Save();
        RefreshDerivedState();
        AddLog($"嘉联益已自动切换为{GetJiaLianYiRunModeName(mode)}：{reason}");
    }

    private void RecordProductionResult(TestFlowSnapshot snapshot)
    {
        ProductionRecords.Insert(0, new ProductionRecordViewModel
        {
            EndTime = snapshot.EndTime ?? DateTime.Now,
            Barcode = snapshot.Barcode,
            Result = snapshot.IsPass == true ? "PASS" : "FAIL",
            FirstFailItem = snapshot.FirstFailItem,
            TestSeconds = Math.Round(snapshot.Elapsed.TotalSeconds, 2),
            ItemCount = snapshot.Items.Count,
            TestItems = snapshot.Items.Select(item => new TestItemResult
            {
                Index = item.Index,
                ItemName = item.ItemName,
                PointA = item.PointA,
                PointB = item.PointB,
                LowerLimit = item.LowerLimit,
                UpperLimit = item.UpperLimit,
                Unit = item.Unit,
                TestValue = item.TestValue,
                Result = item.Result
            }).ToList()
        });

        while (ProductionRecords.Count > 500)
        {
            ProductionRecords.RemoveAt(ProductionRecords.Count - 1);
        }
    }

    private async Task UpdateConsecutiveNgAlarmAsync(TestFlowSnapshot snapshot)
    {
        if (snapshot.IsPass == true)
        {
            CurrentConsecutiveNg = 0;
            return;
        }

        CurrentConsecutiveNg++;
        var threshold = Math.Max(1, _configuration.User.ConsecutiveNgAlarmCount);
        if (CurrentConsecutiveNg >= threshold)
        {
            var message = $"连续 NG 已达到 {CurrentConsecutiveNg} 次，报警阈值：{threshold}。请检查治具、下位机、测试线材或产品状态。";
            AddLog(message);
            await ShowRuntimePopupAsync(message);
        }
    }

    private async Task ToggleOnlineModeAsync()
    {
        if (IsJingWangAccount && (!CanEditJingWangFields || _jwShiftGate.CurrentCount == 0)) return;
        if (_configuration.Mes.OnlineMode)
        {
            if (IsJingWangAccount && !ConfirmJingWangOffline()) return;
            if (MesRuntimeService.IsJingWang(_configuration))
            {
                var logout = await _mesRuntimeService.EndJingWangShiftAsync(_configuration);
                AddLog(logout.IsSuccess ? logout.Message : $"景旺下机记录待处理：{logout.Message}");
            }
            if (IsJingWangAccount) SetJingWangMesSwitch(_configuration, false);
            else _configuration.Mes.OnlineMode = false;
            _jwShiftIdentity = null;
            Save();
            RefreshDerivedState();
            AddLog("已切换为离线模式，MES 卡关和上传将跳过");
            return;
        }

        if (!_configuration.Mes.Enabled)
        {
            _configurationDialogService.ShowAlert("MES 未启用", "请先在 MES 设置中启用 MES 并选择插件。");
            return;
        }

        var onlineConfiguration = _configuration;
        if (IsJingWangAccount)
        {
            onlineConfiguration = ConfigurationStore.Clone(_configuration);
            SetJingWangMesSwitch(onlineConfiguration, true);
        }
        var result = await _mesRuntimeService.TestConnectionAsync(onlineConfiguration);
        if (!result.IsSuccess)
        {
            AddLog($"切换在线失败：{result.Message}");
            _configurationDialogService.ShowAlert("切换在线失败", result.Message);
            return;
        }

        if (MesRuntimeService.IsJingWang(_configuration))
        {
            var login = await _mesRuntimeService.StartJingWangShiftAsync(onlineConfiguration);
            if (!login.IsSuccess)
            {
                AddLog($"景旺员工上机失败：{login.Message}");
                _configurationDialogService.ShowAlert("员工上机失败", login.Message);
                return;
            }
            AddLog(login.Message);
            if (IsJingWangAccount) _jwShiftIdentity = (JwEmployee, JwEquipment);
        }

        if (IsJingWangAccount) SetJingWangMesSwitch(_configuration, true);
        else _configuration.Mes.OnlineMode = true;
        Save();
        RefreshDerivedState();
        AddLog($"已切换为在线模式：{result.Message}");
    }

    public async Task EndJingWangOnExitAsync()
    {
        _jingWangCraftTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (!MesRuntimeService.IsJingWang(_configuration) || !_configuration.Mes.OnlineMode) return;
        var result = await _mesRuntimeService.EndJingWangShiftAsync(_configuration);
        AddLog(result.IsSuccess ? result.Message : $"退出时景旺下机记录待处理：{result.Message}");
        _configuration.Mes.OnlineMode = false;
        _configurationStore.Save(_configuration);
    }

    private void SelectJiaLianYiRunMode(JiaLianYiRunMode mode)
    {
        if (!CanSelectJiaLianYiRunMode)
        {
            _configurationDialogService.ShowAlert("MES 未打开", "请先将主界面的 MES 切换为在线模式，再选择标准片、生产模式或调试模式。");
            return;
        }

        if (_configuration.User.Jly.RunMode == mode)
        {
            return;
        }

        _configuration.User.Jly.RunMode = mode;
        Save();
        RefreshDerivedState();
        AddLog($"昆山嘉联益模式已切换为：{GetJiaLianYiRunModeName(mode)}");
    }

    private void CheckJiaLianYiAutoStandardMode()
    {
        if (!IsKunshanJiaLianYiDedicatedConfiguration)
        {
            return;
        }

        var now = DateTime.Now;
        if ((now.Hour != 8 && now.Hour != 20) || now.Minute != 0 || now.Second > 30)
        {
            return;
        }

        var switchKey = now.ToString("yyyyMMddHH");
        if (string.Equals(_lastJiaLianYiAutoStandardSwitch, switchKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastJiaLianYiAutoStandardSwitch = switchKey;
        if (_configuration.User.Jly.RunMode == JiaLianYiRunMode.StandardPiece)
        {
            return;
        }

        _configuration.User.Jly.RunMode = JiaLianYiRunMode.StandardPiece;
        Save();
        RefreshDerivedState();
        AddLog("到达 08:00 / 20:00，已自动切换为标准片模式");
    }

    private static string GetJiaLianYiRunModeName(JiaLianYiRunMode mode)
    {
        return mode switch
        {
            JiaLianYiRunMode.TestPiece => "生产模式",
            JiaLianYiRunMode.Debug => "调试模式",
            _ => "标准片模式"
        };
    }

    private void ToggleLogVisibility()
    {
        IsLogVisible = !IsLogVisible;
    }

    private void ToggleAdminTools()
    {
        if (!CanUseAdminTools)
        {
            IsAdminToolsVisible = false;
            return;
        }

        IsAdminToolsVisible = !IsAdminToolsVisible;
    }

    private void ClearYield()
    {
        PassCount = 0;
        FailCount = 0;
        CurrentConsecutiveNg = 0;
        ProductionRecords.Clear();
        ClearSavedDailyYieldStatistics();
        AddLog("良率统计和本次运行记录已清零");
    }

    private void RestoreDailyYieldStatistics()
    {
        if (!ShouldPersistYieldStatistics)
        {
            return;
        }

        var statistics = _configuration.User.DailyYieldStatistics ??= new DailyYieldStatistics();
        if (IsKunshanJiaLianYiDedicatedConfiguration)
        {
            PassCount = Math.Max(0, statistics.PassCount);
            FailCount = Math.Max(0, statistics.FailCount);
            AddLog($"已恢复嘉联益累计良率：PASS {PassCount}，FAIL {FailCount}，良率 {YieldRate:F2}%");
            return;
        }

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (!string.Equals(statistics.Date, today, StringComparison.Ordinal))
        {
            statistics.Date = today;
            statistics.PassCount = 0;
            statistics.FailCount = 0;
            _configurationStore.Save(_configuration);
            AddLog("检测到新的日期，当天数量和良率已清零");
            return;
        }

        PassCount = Math.Max(0, statistics.PassCount);
        FailCount = Math.Max(0, statistics.FailCount);
        AddLog($"已恢复当天统计：PASS {PassCount}，FAIL {FailCount}，良率 {YieldRate:F2}%");
    }

    private void PersistDailyYieldStatistics()
    {
        if (!ShouldPersistYieldStatistics)
        {
            return;
        }

        var statistics = _configuration.User.DailyYieldStatistics ??= new DailyYieldStatistics();
        statistics.Date = DateTime.Today.ToString("yyyy-MM-dd");
        statistics.PassCount = PassCount;
        statistics.FailCount = FailCount;
        _configurationStore.Save(_configuration);
    }

    private bool ShouldPersistYieldStatistics => IsKunshanJiaLianYiDedicatedConfiguration
        || _configuration.User.SaveDailyQuantity;

    private void ClearSavedDailyYieldStatistics()
    {
        var statistics = _configuration.User.DailyYieldStatistics ??= new DailyYieldStatistics();
        statistics.Date = DateTime.Today.ToString("yyyy-MM-dd");
        statistics.PassCount = 0;
        statistics.FailCount = 0;
        _configurationStore.Save(_configuration);
    }

    private void Save()
    {
        _configurationStore.Save(_configuration);
        StatusMessage = $"配置已保存：{DateTime.Now:HH:mm:ss}";
        RaiseConfigurationProperties();
    }

    private void RefreshDerivedState()
    {
        MesState = IsJingWangAccount && _configuration.Mes.OnlineMode && !_jwShiftIdentity.HasValue ? "等待 MES 连接" : !_configuration.Mes.Enabled ? "未启用" : _configuration.Mes.OnlineMode ? "在线模式" : "离线模式";
        ScannerState = GetDeviceConnectionState(_configuration.Communication.Scanner, _scannerConnection);
        LowerMachineState = _configuration.Communication.LowerMachine.ConnectionType == ConnectionType.Disabled
            ? "已禁用"
            : _lowerMachineConnection?.IsOpen != true
                ? "未连接"
                : _lowerMachineProtocolConfirmed
                    ? _configuration.Communication.LowerMachineHandshakeEnabled || _configuration.Communication.LowerMachineItemHandshakeEnabled
                        ? "握手成功" : "已连接"
                    : LowerMachineState is "下位机无响应" or "表头请求未发出" or "协议异常" or "报文超时" or "异常" or "已收到表头（未握手）" or "待表头确认" or "表头握手失败"
                        ? LowerMachineState
                        : "待协议确认";
        RaiseConfigurationProperties();
        OnPropertyChanged(nameof(IsManualScanMode));
        OnPropertyChanged(nameof(IsUsbPosHidScanner));
    }

    private static string GetDeviceConnectionState(DeviceConnectionConfiguration configuration, IDeviceConnection? connection)
    {
        if (configuration.ConnectionType == ConnectionType.Disabled)
        {
            return "已禁用";
        }

        return connection?.IsOpen == true ? "已连接" : "未连接";
    }

    private static string GetConnectionStateBrush(string state)
    {
        return state switch
        {
            "已连接" or "握手成功" or "在线模式" => "#22C55E",
            "离线模式" or "已禁用" or "待协议确认" or "待表头确认" or "已收到表头（未握手）" => "#F59E0B",
            _ => "#EF4444"
        };
    }

    private void ApplySnapshot(TestFlowSnapshot snapshot, bool countYield)
    {
        TestState = snapshot.State switch
        {
            TestFlowState.Idle => "WAIT",
            TestFlowState.WaitingScan => "SCAN",
            TestFlowState.Ready => "READY",
            TestFlowState.Testing => "TEST",
            TestFlowState.Completed => snapshot.IsPass == true ? "PASS" : snapshot.IsPass == false ? "FAIL" : "WAIT",
            TestFlowState.Failed => "FAIL",
            _ => snapshot.State.ToString().ToUpperInvariant()
        };

        CurrentBarcode = snapshot.Barcode;
        OnPropertyChanged(nameof(CanEditJingWangFields));
        LastFailItem = string.IsNullOrWhiteSpace(snapshot.FirstFailItem)
            ? string.Empty
            : _configuration.User.HideSpecialItemTestValues && SpecialTestValue.IsSpecial(snapshot.FirstFailItem)
                ? snapshot.FirstFailItem
                : $"{snapshot.FirstFailItem} {snapshot.FirstFailValue}";
        TestTime = $"{snapshot.Elapsed.TotalSeconds:F2}S";
        StatusMessage = snapshot.Message;

        TestItems.Clear();
        foreach (var item in snapshot.Items)
        {
            TestItems.Add(new TestItemViewModel
            {
                Index = item.Index.ToString(),
                ItemName = item.ItemName,
                UpperLimit = item.UpperLimit,
                LowerLimit = item.LowerLimit,
                TestValue = _configuration.User.HideSpecialItemTestValues && SpecialTestValue.IsSpecial(item.ItemName)
                    ? string.Empty : item.TestValue,
                PointA = item.PointA,
                PointB = item.PointB,
                Unit = item.Unit,
                Result = item.Result,
                LimitsChanged = UpdateTestItemLimits
            });
        }

        if (countYield && snapshot.IsPass.HasValue)
        {
            LastTestResult = snapshot.IsPass.Value ? "PASS" : "FAIL";
            if (snapshot.IsPass.Value)
            {
                PassCount++;
            }
            else
            {
                FailCount++;
            }
        }
    }

    private void RaiseConfigurationProperties()
    {
        OnPropertyChanged(nameof(WindowTitle));
        RaiseJingWangFields();
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(CustomerName));
        OnPropertyChanged(nameof(ProductName));
        OnPropertyChanged(nameof(TestType));
        OnPropertyChanged(nameof(TestProgram));
        OnPropertyChanged(nameof(StationNo));
        OnPropertyChanged(nameof(MachineNo));
        OnPropertyChanged(nameof(MesUserNo));
        OnPropertyChanged(nameof(MesPackNo));
        OnPropertyChanged(nameof(MesTooling));
        OnPropertyChanged(nameof(MesProdNo));
        OnPropertyChanged(nameof(IsHongXinMesPluginSelected));
        OnPropertyChanged(nameof(HongXinProductionFlowNo));
        OnPropertyChanged(nameof(CanSelectJiaLianYiRunMode));
        OnPropertyChanged(nameof(IsStandardPieceMode));
        OnPropertyChanged(nameof(IsTestPieceMode));
        OnPropertyChanged(nameof(IsDebugMode));
        OnPropertyChanged(nameof(JiaLianYiRunModeText));
        OnPropertyChanged(nameof(JiaLianYiRunModeBackground));
        OnPropertyChanged(nameof(JiaLianYiRunModeForeground));
        OnPropertyChanged(nameof(MesPlugin));
        OnPropertyChanged(nameof(IsPopupCenteredAndEnlarged));
        OnPropertyChanged(nameof(IsPopupRedAlert));
        OnPropertyChanged(nameof(OnlineModeText));
        OnPropertyChanged(nameof(ScannerSummary));
        OnPropertyChanged(nameof(LowerMachineSummary));
        RaiseSessionProperties();
    }

    private void RaiseSessionProperties()
    {
        OnPropertyChanged(nameof(PopupTitle));
        OnPropertyChanged(nameof(LoginDisplay));
        OnPropertyChanged(nameof(IsYidong2));
        OnPropertyChanged(nameof(DashboardTestState));
        OnPropertyChanged(nameof(CanConfigureStation));
        OnPropertyChanged(nameof(CanManageSystem));
        OnPropertyChanged(nameof(CanConfigureOperatorSettings));
        OnPropertyChanged(nameof(CanOpenBasicConfiguration));
        OnPropertyChanged(nameof(CanOpenCommunicationConfiguration));
        OnPropertyChanged(nameof(CanManageTestItems));
        OnPropertyChanged(nameof(CanManageUsers));
        OnPropertyChanged(nameof(CanManageUsersInStandardMode));
        OnPropertyChanged(nameof(CanImportProjectProfile));
        OnPropertyChanged(nameof(CanViewAnalysis));
        OnPropertyChanged(nameof(CanViewAnalysisInStandardMode));
        OnPropertyChanged(nameof(CanViewHelp));
        OnPropertyChanged(nameof(CanViewHelpInStandardMode));
        OnPropertyChanged(nameof(CanUseAdminTools));
        OnPropertyChanged(nameof(IsKunshanJiaLianYiDedicatedConfiguration));
        OnPropertyChanged(nameof(IsStandardConfigurationMode));
        if (!CanUseAdminTools)
        {
            IsAdminToolsVisible = false;
        }
    }

    private void RaiseYieldProperties()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PassFailText));
        OnPropertyChanged(nameof(YieldRate));
        OnPropertyChanged(nameof(YieldRateText));
    }

    private void UpdateTestItemLimits(TestItemViewModel item)
    {
        if (!int.TryParse(item.Index, out var index))
        {
            return;
        }

        var configuration = _configuration.TestItems.Items
            .OrderBy(configItem => configItem.Order)
            .ElementAtOrDefault(index - 1);
        if (configuration is null)
        {
            return;
        }

        configuration.LowerLimit = item.LowerLimit;
        configuration.UpperLimit = item.UpperLimit;
        Save();
    }

    private void SyncConfiguredTestItems(TestFlowSnapshot snapshot)
    {
        if (snapshot.Items.Count == 0)
        {
            return;
        }

        var syncedItems = snapshot.Items
            .OrderBy(item => item.Index)
            .Select((item, index) => new TestItemConfiguration
            {
                Order = index + 1,
                ItemName = item.ItemName,
                UpperLimit = item.UpperLimit,
                LowerLimit = item.LowerLimit,
                Unit = item.Unit,
                PointA = item.PointA,
                PointB = item.PointB
            })
            .ToList();

        if (AreSameTestItems(_configuration.TestItems.Items, syncedItems))
        {
            return;
        }

        _configuration.TestItems.Items = syncedItems;
        _configurationStore.Save(_configuration);
    }

    private static bool AreSameTestItems(IReadOnlyList<TestItemConfiguration> currentItems, IReadOnlyList<TestItemConfiguration> syncedItems)
    {
        var orderedCurrentItems = currentItems.OrderBy(item => item.Order).ToList();
        if (orderedCurrentItems.Count != syncedItems.Count)
        {
            return false;
        }

        for (var index = 0; index < orderedCurrentItems.Count; index++)
        {
            var current = orderedCurrentItems[index];
            var synced = syncedItems[index];
            if (current.Order != synced.Order
                || current.ItemName != synced.ItemName
                || current.UpperLimit != synced.UpperLimit
                || current.LowerLimit != synced.LowerLimit
                || current.Unit != synced.Unit
                || current.PointA != synced.PointA
                || current.PointB != synced.PointB)
            {
                return false;
            }
        }

        return true;
    }

    private Task ShowRuntimePopupAsync(string message, bool force = false, bool? requiresConfirmation = null)
    {
        if (_authenticationService.CurrentSession.IsDiagnosticMode && !message.StartsWith("【", StringComparison.Ordinal))
        {
            var category = message.Contains("MES", StringComparison.OrdinalIgnoreCase) ? "MES / 网络 / 业务校验"
                : message.Contains("NoRead", StringComparison.OrdinalIgnoreCase) ? "扫码设备 / 读码失败"
                : message.Contains("条码", StringComparison.Ordinal) ? "条码 / 配置校验"
                : "上位机 / 业务提示";
            message = $"【{category}】\n{message}";
        }
        if ((!force && !_configuration.Popup.Enabled && !_authenticationService.CurrentSession.IsDiagnosticMode) || string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        var popup = new PopupNotification(message, requiresConfirmation ?? _configuration.Popup.RequireConfirmation);
        _popupQueue.Enqueue(popup);
        ShowNextPopup();
        return popup.Completion.Task;
    }

    private void ClearSessionPopups()
    {
        while (_popupQueue.TryDequeue(out var popup))
        {
            popup.Completion.TrySetResult();
        }

        _activePopup?.Completion.TrySetResult();
        _activePopup = null;
        IsPopupVisible = false;
        PopupMessage = string.Empty;
    }

    private void ShowNextPopup()
    {
        if (_activePopup is not null || !_popupQueue.TryDequeue(out var popup))
        {
            return;
        }

        _activePopup = popup;
        PopupMessage = popup.Message;
        IsPopupVisible = true;
        if (!popup.RequiresConfirmation)
        {
            _ = HidePopupAfterDelayAsync(popup);
        }
    }

    private async Task HidePopupAfterDelayAsync(PopupNotification popup)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (!ReferenceEquals(_activePopup, popup) || popup.RequiresConfirmation)
        {
            return;
        }

        DismissActivePopup();
    }

    private void ConfirmPopup()
    {
        if (_activePopup is null)
        {
            return;
        }

        DismissActivePopup();
    }

    private void DismissActivePopup()
    {
        var popup = _activePopup;
        if (popup is null)
        {
            return;
        }

        _activePopup = null;
        IsPopupVisible = false;
        popup.Completion.TrySetResult();
        ShowNextPopup();
    }

    private sealed class PopupNotification(string message, bool requiresConfirmation)
    {
        public string Message { get; } = message;
        public bool RequiresConfirmation { get; } = requiresConfirmation;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private void AddLog(string message)
    {
        var timestamp = DateTime.Now;
        Logs.Insert(0, $"{timestamp:HH:mm:ss}  {CH_V03.App.Localization.UiLanguage.Text(message)}");
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }

        _runtimeLogWriter.Write(_configuration.Output.LogDirectory, timestamp, message);
    }

    private void AddDiagnosticLog(string category, string message, string suggestion)
    {
        AddLog(message);
        if (UseJingWangRecords)
        {
            var code = category.StartsWith("扫码设备", StringComparison.Ordinal) ? "SCANNER_FAILURE"
                : category.StartsWith("下位机", StringComparison.Ordinal) ? "LOWER_MACHINE_FAILURE"
                : category.StartsWith("上位机", StringComparison.Ordinal) ? "HOST_PROTOCOL_FAILURE" : string.Empty;
            if (!string.IsNullOrEmpty(code)) _ = RaiseJingWangAlarmAsync(code, category);
        }
        if (!_authenticationService.CurrentSession.IsDiagnosticMode)
        {
            return;
        }

        var diagnostic = $"【{category}】\n{message}\n排查建议：{suggestion}";
        if (_activePopup?.Message == diagnostic || _popupQueue.Any(popup => popup.Message == diagnostic))
        {
            return;
        }

        _ = ShowRuntimePopupAsync(diagnostic, force: true, requiresConfirmation: true);
    }

    private async Task RaiseJingWangAlarmAsync(string code, string description)
    {
        if (!_jingWangActiveAlarms.Add(code)) return;
        try
        {
            var result = await _mesRuntimeService.RecordJingWangAlarmAsync(_configuration, code, description, true);
            if (!result.IsSuccess) AddLog($"景旺报警待写入：{result.Message}");
        }
        catch (Exception ex) { AddLog($"景旺报警待写入：{ex.GetType().Name}"); }
    }

    private async Task ClearJingWangAlarmAsync(string code, string description)
    {
        if (!_jingWangActiveAlarms.Remove(code)) return;
        try
        {
            var result = await _mesRuntimeService.RecordJingWangAlarmAsync(_configuration, code, description, false);
            if (!result.IsSuccess) AddLog($"景旺报警复位待写入：{result.Message}");
        }
        catch (Exception ex) { AddLog($"景旺报警复位待写入：{ex.GetType().Name}"); }
    }

    private async Task CollectJingWangCraftIfDueAsync()
    {
        if (!_configuration.Mes.Enabled || !UseJingWangRecords || _jingWangLatestCraftSnapshot is null) return;
        var text = _configuration.Mes.PluginOptions.GetValueOrDefault("CraftMinutes", "5");
        var minutes = int.TryParse(text, out var configured) ? Math.Clamp(configured, 1, 1440) : 5;
        var snapshot = _jingWangLatestCraftSnapshot;
        if (snapshot.EndTime is null || !_jingWangCraftSchedule.TryBegin(DateTime.Now, snapshot.EndTime.Value, minutes)) return;
        var accepted = false;
        try
        {
            var result = await _mesRuntimeService.CollectJingWangCraftAsync(_configuration, snapshot);
            AddLog(result.IsSuccess ? result.Message : $"景旺工艺采集待处理：{result.Message}");
            accepted = result.IsSuccess || result.Code == "PENDING_DATABASE";
        }
        catch (Exception ex) { AddLog($"景旺工艺采集待处理：{ex.GetType().Name}"); }
        finally { _jingWangCraftSchedule.Complete(snapshot.EndTime.Value, accepted); }
    }

    private void PostToUi(Action action)
    {
        _uiContext.Post(_ => action(), null);
    }

    private string BuildSampleBarcode()
    {
        var suffix = _configuration.Identity.CodeSuffix ?? string.Empty;
        if (!int.TryParse(_configuration.Identity.BarcodeLength, out var length) || length <= 0)
        {
            length = 12;
        }

        var baseCode = "000000000001";
        if (!string.IsNullOrWhiteSpace(suffix) && suffix.Length <= length)
        {
            baseCode = new string('0', Math.Max(0, length - suffix.Length)) + suffix;
        }

        return baseCode.Length > length ? baseCode[^length..] : baseCode.PadLeft(length, '0');
    }

    private string GetMesPluginDisplayName()
    {
        if (!_configuration.Mes.Enabled || string.Equals(_configuration.Mes.SelectedPlugin, "None", StringComparison.OrdinalIgnoreCase))
        {
            return "未加载 MES 插件";
        }

        var plugin = _mesRuntimeService.Plugins.FirstOrDefault(item => string.Equals(item.Id, _configuration.Mes.SelectedPlugin, StringComparison.OrdinalIgnoreCase));
        return plugin is null ? _configuration.Mes.SelectedPlugin : plugin.Name;
    }
    private string BuildMesPluginLog()
    {
        if (_mesRuntimeService.Plugins.Count == 0)
        {
            return "MES插件加载失败：未找到可用插件";
        }

        return $"MES插件加载成功：{_mesRuntimeService.Plugins.Count} 个";
    }

    private string BuildExportPluginLog()
    {
        if (_exportRuntimeService.Plugins.Count == 0)
        {
            return "导出插件加载失败：未找到可用插件";
        }

        return $"导出插件加载成功：{_exportRuntimeService.Plugins.Count} 个";
    }

    private string BuildProjectProfileLog()
    {
        if (_projectProfiles.Count == 0)
        {
            return "项目Profile目录无可用模板";
        }

        return $"项目Profile加载成功：{_projectProfiles.Count} 个";
    }

    private static string DecodeCommand(string command)
    {
        return (command ?? string.Empty)
            .Replace("\\r", "\r", StringComparison.OrdinalIgnoreCase)
            .Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("\\t", "\t", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatCommandForLog(string command)
    {
        return command.Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static string NormalizeIncomingText(string text)
    {
        return (text ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
    }

    private static string FormatDevice(DeviceConnectionConfiguration configuration)
    {
        return configuration.ConnectionType switch
        {
            ConnectionType.SerialPort => $"{configuration.PortName} / {configuration.BaudRate}",
            ConnectionType.TcpClient or ConnectionType.TcpServer => $"{configuration.IpAddress}:{configuration.Port}",
            ConnectionType.UsbPosHid => "USB POS HID",
            ConnectionType.CameraQr => $"USB Video Device / 摄像头 {configuration.CameraIndex} / {configuration.CameraWidth} × {configuration.CameraHeight}",
            ConnectionType.Disabled => "已禁用",
            _ => configuration.ConnectionType.ToString()
        };
    }
}






