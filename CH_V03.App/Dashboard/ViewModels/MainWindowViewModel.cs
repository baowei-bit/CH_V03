using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.App.Services;
using CH_V03.Core.Communication;
using CH_V03.Core.Configuration;
using CH_V03.Core.Mes;
using CH_V03.Core.ResultExport;
using CH_V03.Core.TestFlow;

namespace CH_V03.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
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
    private const int TestItemRequestRetryDelayMilliseconds = 1000;
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
    private bool _waitingForItemHeader;
    private bool _itemHeaderReceptionStarted;
    private bool _awaitingLowerMachineRestartAfterReset;
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
        var appliedJiaLianYiDefaults = ApplyKunshanJiaLianYiDedicatedDefaults(_configuration);
        var startedInJiaLianYiProductionMode = ApplyJiaLianYiStartupProductionMode(_configuration);
        NormalizeScanMode(_configuration.User);
        if (appliedJiaLianYiDefaults || startedInJiaLianYiProductionMode)
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
        PostToUi(() => _ = AutoConnectConfiguredDevicesAsync());
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
    public string HeaderTitle => $"{_configuration.Identity.ProductName}-{_configuration.Identity.TestType}-{_configuration.Identity.TestProgram}";
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
    public string LoginDisplay => $"{_authenticationService.CurrentSession.DisplayName} / {_authenticationService.CurrentSession.RoleText}";

    public string PopupTitle => _authenticationService.CurrentSession.IsDiagnosticMode ? "设备 / 上位机诊断信息" : "MES / 系统消息提醒";
    public bool CanManageSystem => _authenticationService.CurrentSession.CanManageSystem;
    public bool CanConfigureOperatorSettings => _authenticationService.CurrentSession.IsAuthenticated
        && _authenticationService.CurrentSession.Role == UserRole.Operator;
    public bool CanOpenBasicConfiguration => CanManageSystem || CanConfigureOperatorSettings;
    public bool CanOpenCommunicationConfiguration => CanManageSystem || CanConfigureOperatorSettings;
    public bool CanManageTestItems => _authenticationService.CurrentSession.CanManageTestItems;
    public bool CanManageUsers => _authenticationService.CurrentSession.CanManageUsers;
    public bool CanManageUsersInStandardMode => CanManageUsers && IsStandardConfigurationMode;
    public bool CanImportProjectProfile => _authenticationService.CurrentSession.CanImportProjectProfile;
    public bool CanViewAnalysis => _authenticationService.CurrentSession.CanViewAnalysis;
    public bool CanViewAnalysisInStandardMode => CanViewAnalysis && IsStandardConfigurationMode;
    public bool CanViewHelp => _authenticationService.CurrentSession.IsAuthenticated
        && _authenticationService.CurrentSession.Role != UserRole.Operator;
    public bool CanViewHelpInStandardMode => CanViewHelp && IsStandardConfigurationMode;
    public bool CanUseAdminTools => _authenticationService.CurrentSession.Role == UserRole.Administrator;
    public bool IsKunshanJiaLianYiDedicatedConfiguration => _configuration.Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration;
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
        private set => SetField(ref _testState, value);
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
            : _configurationDialogService.ShowBasicConfiguration(editable, CanManageSystem);
        if (!saved)
        {
            StatusMessage = "已取消基础配置";
            return;
        }

        if (CanConfigureOperatorSettings && !IsKunshanJiaLianYiDedicatedConfiguration)
        {
            // 即使未来基础配置窗口新增控件，操作员保存时也只能覆盖保存路径。
            var output = editable.Output;
            editable = ConfigurationStore.Clone(_configuration);
            editable.Output = output;
        }

        editable.Communication.LowerMachinePayloadTimeoutSeconds = Math.Max(
            1,
            editable.Communication.LowerMachinePayloadTimeoutSeconds);
        editable.Communication.ScannerWaitTimeoutSeconds = Math.Max(
            1,
            editable.Communication.ScannerWaitTimeoutSeconds);
        _configuration = editable;
        Save();
        RefreshDerivedState();
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
        _configuration = editable;
        Save();
        RefreshDerivedState();
        AddLog("通讯设置已更新");
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
        if (!CanManageSystem)
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
        ApplyKunshanJiaLianYiDedicatedDefaults(imported);

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

        var canManageAccounts = _authenticationService.CurrentSession.Role == UserRole.Administrator;
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

        var switchedToManualScan = !_configuration.User.ManualScanMode && editable.ManualScanMode;
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

            Save();
            ClearSessionPopups();
            AddLog($"用户登录：{LoginDisplay}");
            RaiseSessionProperties();
            return true;
        }

        return false;
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
            await _lowerMachineConnection.OpenAsync();
            LowerMachineState = _configuration.Communication.LowerMachine.ConnectionType == ConnectionType.Disabled ? "已禁用" : "已连接";
            AddLog($"下位机连接完成：{LowerMachineSummary}");
            StartTestItemRequestRetry(waitForAutomaticHeader: false);
            return true;
        }
        catch (Exception ex)
        {
            if (_lowerMachineConnection is not null)
            {
                _lowerMachineConnection.DataReceived -= LowerMachineConnection_DataReceived;
                await _lowerMachineConnection.DisposeAsync();
                _lowerMachineConnection = null;
            }

            LowerMachineState = "异常";
            AddDiagnosticLog("下位机 / 连接失败", $"下位机连接失败：{ex.Message}", "检查下位机供电、通讯参数及端口占用。");
            return false;
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
        ResetLowerMachineReceiveBuffer();
        CancelScannerWaitTimeout();
        CancelTestItemRequestRetry();
        _waitingForItemHeader = false;
        _awaitingLowerMachineRestartAfterReset = false;
        _startTestAfterItemHeader = false;
        _discardLowerMachineDataUntilNextStartSignal = false;
        _scanAheadStatus = ScanAheadStatus.None;
        if (_lowerMachineConnection is not null)
        {
            _lowerMachineConnection.DataReceived -= LowerMachineConnection_DataReceived;
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
        var result = await _communicationTestService.TestAsync(_configuration.Communication.LowerMachine);
        LowerMachineState = result.IsSuccess ? "已连接" : "异常";
        StatusMessage = result.Message;
        AddLog(result.Message);
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
        PostToUi(() => HandleLowerMachineText(e.Text));
    }

    private async Task HandleScannerTextAsync(string text)
    {
        try
        {
            await HandleScannerTextCoreAsync(text);
        }
        catch (Exception ex)
        {
            AddDiagnosticLog("上位机 / 扫码处理异常", $"扫码数据处理失败：{ex.GetType().Name}：{ex.Message}", "检查条码、配置及相关服务日志。");
        }
    }

    private async Task HandleScannerTextCoreAsync(string text)
    {
        var barcode = NormalizeIncomingText(text);
        if (string.IsNullOrWhiteSpace(barcode))
        {
            AddLog("扫码枪空数据已忽略");
            return;
        }

        CancelScannerWaitTimeout();
        AddLog($"RecScanner:{barcode}");
        await StopScannerAsync();

        await WaitForJiaLianYiMesUploadAsync();

        if (!_configuration.Identity.AllowContinuousRepeatTest
            && string.Equals(barcode, _lastTestStartedBarcode, StringComparison.OrdinalIgnoreCase))
        {
            const string message = "条码与上一笔测试相同，未启用连续重复测试，已阻止本次测试";
            StatusMessage = message;
            AddLog($"{message}：{barcode}");
            await ShowRuntimePopupAsync(message);
            return;
        }

        var snapshot = _testFlowService.AcceptBarcode(barcode, TestFlowOptions.FromConfiguration(_configuration));
        ApplySnapshot(snapshot, countYield: false);

        if (snapshot.State == TestFlowState.Ready)
        {
            var compatibilityError = ApplyCompatibilityBeforeTest(barcode);
            if (!string.IsNullOrWhiteSpace(compatibilityError))
            {
                await ShowRuntimePopupAsync(compatibilityError);
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
                var mesResult = await _mesRuntimeService.CheckBeforeTestAsync(_configuration, barcode);
                AddLog(mesResult.IsSuccess ? $"MES卡关通过：{mesResult.Message}" : $"MES卡关失败：{mesResult.Message}");
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
                    await ShowRuntimePopupAsync($"MES卡关失败：{mesResult.Message}");
                    await RecordScanFailureAsync();
                    return;
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
            await ShowRuntimePopupAsync(snapshot.Message);
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
                AddLog("急停后收到的旧周期下位机数据已忽略，等待新的 DM 信号");
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
                if (_lowerMachineReceiveBuffer.Length > 8192)
                {
                    AddDiagnosticLog("下位机 / 报文异常", "下位机接收缓存超过 8192 字符，已清空", "检查报文长度、分隔符和结束标记 end。");
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
            _lowerMachineStartSignalTime = DateTime.Now;
            var requestHeaderAfterReset = _awaitingLowerMachineRestartAfterReset;
            _awaitingLowerMachineRestartAfterReset = false;
            if (requestHeaderAfterReset)
            {
                InitializeFlow();
                _startTestAfterItemHeader = true;
                StartTestItemRequestRetry(waitForAutomaticHeader: false);
                AddLog("下位机复位后的 DM 已到达，正在请求 Item1 表头；收到表头后才会发送 DMOK 或启动测试");
                return;
            }

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
            AddLog($"sendLowerCom:{FormatCommandForLog(command)}（表头请求 {attempt}/{TestItemRequestMaxAttempts}）");
        }
    }

    private void StartTestItemRequestRetry(bool waitForAutomaticHeader)
    {
        CancelTestItemRequestRetry();
        if (!_waitingForItemHeader || !_configuration.Communication.TestItemRequestEnabled)
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
            if (waitForAutomaticHeader)
            {
                await Task.Delay(TestItemRequestRetryDelayMilliseconds, retryCts.Token);
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

                await Task.Delay(TestItemRequestRetryDelayMilliseconds, retryCts.Token);
            }

            if (ReferenceEquals(_testItemRequestRetryCts, retryCts) && _waitingForItemHeader)
            {
                AddDiagnosticLog("下位机 / 未接收到表头", $"已完成 {TestItemRequestMaxAttempts} 次表头请求尝试（每次等待 {TestItemRequestRetryDelayMilliseconds / 1000.0:g} 秒），仍未收到 Item1 表头；继续等待表头，不接收残留 Test 数据", "查看发送日志确认请求是否发出，检查下位机响应、通讯线及 Item1...;end 报文格式。");
            }
        }
        catch (OperationCanceledException) when (retryCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
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

        foreach (var command in GetLowerMachineOkCommands(barcode))
        {
            if (!await SendLowerMachineAsync(command))
            {
                return;
            }
            AddLog($"sendLowerCom:{command.Trim()}");
        }

        _lastTestStartedBarcode = barcode;
        _scanAheadStatus = ScanAheadStatus.None;
        ApplySnapshot(_testFlowService.StartTest(), countYield: false);
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
        if (!UsesBarcodeLbRouting())
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
        ResetLowerMachineReceiveBuffer();
        CancelScannerWaitTimeout();
        _ignoreScannerDataUntilNextStartSignal = true;
        _waitingForItemHeader = true;
        _awaitingLowerMachineRestartAfterReset = true;
        _startTestAfterItemHeader = false;
        _discardLowerMachineDataUntilNextStartSignal = false;
        _scanAheadStatus = ScanAheadStatus.None;
        _pendingJlyRetestFailure = null;
        _lowerMachineStartSignalTime = null;

        var snapshot = _testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration));
        ApplySnapshot(snapshot, countYield: false);
        AddDiagnosticLog("下位机 / 复位或急停", "收到下位机 reset：当前测试数据已作废；等待下位机重新发送 DM 后再请求 Item1 表头", "检查急停和复位状态，恢复后重新启动测试。");
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
            _waitingForItemHeader = true;
            _awaitingLowerMachineRestartAfterReset = false;
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

        var snapshot = _testFlowService.AcceptBarcode(barcode, new TestFlowOptions
        {
            AutoScan = false,
            NoScanMode = true
        });
        ApplySnapshot(snapshot, countYield: false);

        foreach (var command in GetLowerMachineOkCommands(barcode))
        {
            if (!await SendLowerMachineAsync(command))
            {
                return;
            }
            AddLog($"sendLowerCom:{command.Trim()}");
        }

        ApplySnapshot(_testFlowService.StartTest(), countYield: false);
    }

    private async Task CompleteByPayloadAsync(string payload, bool countYield)
    {
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
            AddDiagnosticLog("上位机 / 结果解析错误", $"测试结果解析失败：{ex.GetType().Name}：{ex.Message}", "检查原始结果报文、字段和编号；本报文未计入产量、未保存为测试结果、未上传 MES。");
            return;
        }
        _lowerMachineStartSignalTime = null;
        ApplySnapshot(snapshot, countYield);
        AddLog(snapshot.Message);

        if (snapshot.IsPass.HasValue)
        {
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
                if (snapshot.IsPass == false && _configuration.User.Jly.ShowFailResultPopup)
                {
                    await ShowRuntimePopupAsync("测试结果：FAIL", requiresConfirmation: false);
                }

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
    }

    private async Task<IReadOnlyList<ResultExportResult>> ExportResultAsync(TestFlowSnapshot snapshot)
    {
        var results = await _exportRuntimeService.ExportAsync(_configuration, snapshot);

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
            var fileName = $"{output.CsvName}_{DateTime.Now:yyyyMMdd}.csv";
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
        if (IsJiaLianYiDebugMode)
        {
            AddLog("嘉联益调试模式：已跳过 MES 上传");
            return;
        }

        if (!_configuration.Mes.Enabled || !_configuration.Mes.OnlineMode)
        {
            return;
        }

        if (_configuration.User.Jly.RecordSecondTestYield)
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
            var mesCsvFiles = GetMesCsvFiles(exportResults, _configuration, snapshot, out var csvDiagnostics);
            AddLog(csvDiagnostics);
            result = await _mesRuntimeService.UploadResultAsync(_configuration, snapshot, mesCsvFiles);
        }

        if (string.Equals(result.Code, "DUPLICATE", StringComparison.OrdinalIgnoreCase))
        {
            AddLog($"MES上传跳过：{result.Message}");
            return;
        }

        var message = result.IsSuccess ? $"MES上传成功：{result.Message}" : $"MES上传失败：{result.Message}";
        AddLog(message);
        ApplyJiaLianYiModeFromMesResponse(result, "MES 上传");
        if (!result.IsSuccess)
        {
            if (!string.IsNullOrWhiteSpace(result.Diagnostics))
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
        if (!_configuration.User.Jly.TestPieceMode || snapshot.IsPass == true)
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
            ItemCount = snapshot.Items.Count
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
        if (_configuration.Mes.OnlineMode)
        {
            _configuration.Mes.OnlineMode = false;
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

        var result = await _mesRuntimeService.TestConnectionAsync(_configuration);
        if (!result.IsSuccess)
        {
            AddLog($"切换在线失败：{result.Message}");
            _configurationDialogService.ShowAlert("切换在线失败", result.Message);
            return;
        }

        _configuration.Mes.OnlineMode = true;
        Save();
        RefreshDerivedState();
        AddLog($"已切换为在线模式：{result.Message}");
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
        MesState = !_configuration.Mes.Enabled ? "未启用" : _configuration.Mes.OnlineMode ? "在线模式" : "离线模式";
        ScannerState = GetDeviceConnectionState(_configuration.Communication.Scanner, _scannerConnection);
        LowerMachineState = GetDeviceConnectionState(_configuration.Communication.LowerMachine, _lowerMachineConnection);
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
            "已连接" or "在线模式" => "#22C55E",
            "离线模式" or "已禁用" => "#F59E0B",
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
            TestFlowState.Completed => "PASS",
            TestFlowState.Failed => "FAIL",
            _ => snapshot.State.ToString().ToUpperInvariant()
        };

        CurrentBarcode = snapshot.Barcode;
        LastFailItem = string.IsNullOrWhiteSpace(snapshot.FirstFailItem) ? string.Empty : $"{snapshot.FirstFailItem} {snapshot.FirstFailValue}";
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
                TestValue = item.TestValue,
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
        Logs.Insert(0, $"{timestamp:HH:mm:ss}  {message}");
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }

        _runtimeLogWriter.Write(_configuration.Output.LogDirectory, timestamp, message);
    }

    private void AddDiagnosticLog(string category, string message, string suggestion)
    {
        AddLog(message);
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






