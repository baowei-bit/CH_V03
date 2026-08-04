using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using CH_V01.App.Commands;
using CH_V01.App.Services;
using CH_V01.Core.Communication;
using CH_V01.Core.Configuration;
using CH_V01.Core.Mes;
using CH_V01.Core.ResultExport;
using CH_V01.Core.TestFlow;

namespace CH_V01.App.ViewModels;

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
    private string _lastHandledScannerBarcode = string.Empty;
    private string _lowerMachineReceiveBuffer = string.Empty;
    private bool _waitingForItemHeader;
    private ScanAheadStatus _scanAheadStatus;
    private CancellationTokenSource? _scannerReceiveDebounceCts;
    private CancellationTokenSource? _lowerMachinePayloadTimeoutCts;
    private readonly Queue<PopupNotification> _popupQueue = new();
    private PopupNotification? _activePopup;
    private TestFlowSnapshot? _pendingJlyRetestFailure;

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
        NormalizeScanMode(_configuration.User);

        OpenBasicConfigurationCommand = new RelayCommand(OpenBasicConfiguration);
        OpenCommunicationConfigurationCommand = new RelayCommand(OpenCommunicationConfiguration);
        OpenMesConfigurationCommand = new RelayCommand(OpenMesConfiguration);
        OpenMesDebugToolCommand = new RelayCommand(OpenMesDebugTool);
        OpenPluginConfigurationCommand = new RelayCommand(OpenPluginConfiguration);
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
        AddLog($"协议适配器已加载：{_protocolAdapter.Name}({_protocolAdapter.Id})");
        AddLog(BuildProjectProfileLog());
        AddLog(BuildMesPluginLog());
        AddLog(BuildExportPluginLog());
        PostToUi(() => _ = AutoConnectConfiguredDevicesAsync());
    }

    public ICommand OpenBasicConfigurationCommand { get; }
    public ICommand OpenCommunicationConfigurationCommand { get; }
    public ICommand OpenMesConfigurationCommand { get; }
    public ICommand OpenMesDebugToolCommand { get; }
    public ICommand OpenPluginConfigurationCommand { get; }
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
        get => _configuration.Identity.StationNo;
        set
        {
            if (string.Equals(_configuration.Identity.StationNo, value, StringComparison.Ordinal))
            {
                return;
            }

            _configuration.Identity.StationNo = value ?? string.Empty;
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
    public string MesPlugin => GetMesPluginDisplayName();
    public string OnlineModeText => _configuration.Mes.OnlineMode ? "切换离线" : "切换在线";
    public string LogToggleText => IsLogVisible ? "隐藏日志" : "显示日志";
    public string ScannerSummary => FormatDevice(_configuration.Communication.Scanner);
    public string LowerMachineSummary => FormatDevice(_configuration.Communication.LowerMachine);
    public string LoginDisplay => $"{_authenticationService.CurrentSession.DisplayName} / {_authenticationService.CurrentSession.RoleText}";
    public bool CanManageSystem => _authenticationService.CurrentSession.CanManageSystem;
    public bool CanConfigureOperatorSettings => _authenticationService.CurrentSession.IsAuthenticated
        && _authenticationService.CurrentSession.Role == UserRole.Operator;
    public bool CanOpenBasicConfiguration => CanManageSystem || CanConfigureOperatorSettings;
    public bool CanOpenCommunicationConfiguration => CanManageSystem || CanConfigureOperatorSettings;
    public bool CanManageTestItems => _authenticationService.CurrentSession.CanManageTestItems;
    public bool CanManageUsers => _authenticationService.CurrentSession.CanManageUsers;
    public bool CanImportProjectProfile => _authenticationService.CurrentSession.CanImportProjectProfile;
    public bool CanViewAnalysis => _authenticationService.CurrentSession.CanViewAnalysis;
    public bool CanViewHelp => _authenticationService.CurrentSession.IsAuthenticated
        && _authenticationService.CurrentSession.Role != UserRole.Operator;
    public bool CanUseAdminTools => _authenticationService.CurrentSession.Role == UserRole.Administrator;
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
        if (!_configurationDialogService.ShowBasicConfiguration(editable, CanManageSystem))
        {
            StatusMessage = "已取消基础配置";
            return;
        }

        if (CanConfigureOperatorSettings)
        {
            // 即使未来基础配置窗口新增控件，操作员保存时也只能覆盖保存路径。
            var output = editable.Output;
            editable = ConfigurationStore.Clone(_configuration);
            editable.Output = output;
        }

        editable.Communication.LowerMachinePayloadTimeoutSeconds = Math.Max(
            1,
            editable.Communication.LowerMachinePayloadTimeoutSeconds);
        _configuration = editable;
        Save();
        RefreshDerivedState();
        AddLog("基础配置已更新");
    }

    private void OpenCommunicationConfiguration()
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

        _configuration = editable;
        Save();
        RefreshDerivedState();
        AddLog("通讯设置已更新");
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
        var canManageAccounts = _authenticationService.CurrentSession.Role == UserRole.Administrator;
        if (!_configurationDialogService.ShowUserConfiguration(editable, _configuration.Compatibility, canManageAccounts))
        {
            StatusMessage = "已取消用户配置";
            return;
        }

        if (!canManageAccounts)
        {
            editable.Accounts = ConfigurationStore.Clone(_configuration.User.Accounts);
        }

        var wasSavingDailyQuantity = _configuration.User.SaveDailyQuantity;
        _configuration.User = editable;
        NormalizeScanMode(_configuration.User);
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
        AddLog("用户配置已更新");
    }

    private void SwitchLogin()
    {
        ShowLogin();
    }

    public bool ShowLogin()
    {
        if (_configuration.DefaultOperatorLogin)
        {
            if (!_authenticationService.LoginAsDefaultOperator(_configuration.User, out var message))
            {
                _configurationDialogService.ShowAlert("自动登录失败", message);
                return false;
            }

            AddLog(message);
            RaiseSessionProperties();
            return true;
        }

        if (_configurationDialogService.ShowLogin(_authenticationService, _configuration.User))
        {
            Save();
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
            AddLog($"扫码枪连接失败：{ex.Message}");
            return false;
        }
    }

    private async Task<bool> ConnectLowerMachineAsync()
    {
        try
        {
            ResetLowerMachineReceiveBuffer();
            _waitingForItemHeader = true;
            _lowerMachineConnection = _deviceConnectionFactory.Create(_configuration.Communication.LowerMachine);
            _lowerMachineConnection.DataReceived += LowerMachineConnection_DataReceived;
            await _lowerMachineConnection.OpenAsync();
            LowerMachineState = _configuration.Communication.LowerMachine.ConnectionType == ConnectionType.Disabled ? "已禁用" : "已连接";
            AddLog($"下位机连接完成：{LowerMachineSummary}");
            await RequestTestItemsAsync();
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
            AddLog($"下位机连接失败：{ex.Message}");
            return false;
        }
    }

    private async Task DisconnectDevicesAsync()
    {
        ResetScannerReceiveBuffer();
        _lastHandledScannerBarcode = string.Empty;

        await DisconnectScannerAsync();
        await DisconnectLowerMachineAsync();

        RefreshDerivedState();
        AddLog("设备连接已断开");
    }

    private async Task DisconnectScannerAsync()
    {
        ResetScannerReceiveBuffer();
        _lastHandledScannerBarcode = string.Empty;

        if (_scannerConnection is not null)
        {
            _scannerConnection.DataReceived -= ScannerConnection_DataReceived;
            await _scannerConnection.DisposeAsync();
            _scannerConnection = null;
        }
    }

    private async Task DisconnectLowerMachineAsync()
    {
        ResetLowerMachineReceiveBuffer();
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

    private void LowerMachineConnection_DataReceived(object? sender, DeviceDataReceivedEventArgs e)
    {
        PostToUi(() => HandleLowerMachineText(e.Text));
    }

    private async Task HandleScannerTextAsync(string text)
    {
        var barcode = NormalizeIncomingText(text);
        if (string.IsNullOrWhiteSpace(barcode))
        {
            AddLog("扫码枪空数据已忽略");
            return;
        }

        AddLog($"RecScanner:{barcode}");
        await StopScannerAsync();
        if (IsRepeatedScannerBarcode(barcode))
        {
            AddLog($"重复扫码已忽略：{barcode}");
            return;
        }

        var snapshot = _testFlowService.AcceptBarcode(barcode, TestFlowOptions.FromConfiguration(_configuration));
        ApplySnapshot(snapshot, countYield: false);

        if (snapshot.State == TestFlowState.Ready)
        {
            _lastHandledScannerBarcode = barcode;
            var compatibilityError = ApplyCompatibilityBeforeTest(barcode);
            if (!string.IsNullOrWhiteSpace(compatibilityError))
            {
                await ShowRuntimePopupAsync(compatibilityError);
                await RecordScanFailureAsync();
                AddLog(compatibilityError);
                return;
            }

            if (_configuration.Mes.Enabled && _configuration.Mes.OnlineMode)
            {
                var mesResult = await _mesRuntimeService.CheckBeforeTestAsync(_configuration, barcode);
                AddLog(mesResult.IsSuccess ? $"MES卡关通过：{mesResult.Message}" : $"MES卡关失败：{mesResult.Message}");
                if (!string.IsNullOrWhiteSpace(mesResult.Diagnostics))
                {
                    AddLog(mesResult.Diagnostics);
                }
                if (!mesResult.IsSuccess)
                {
                    await ShowRuntimePopupAsync($"MES卡关失败：{mesResult.Message}");
                    await RecordScanFailureAsync();
                    return;
                }
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

    private bool IsRepeatedScannerBarcode(string barcode)
    {
        barcode = NormalizeIncomingText(barcode);
        if (string.IsNullOrWhiteSpace(barcode) || string.IsNullOrWhiteSpace(_lastHandledScannerBarcode))
        {
            return false;
        }

        return string.Equals(barcode, _lastHandledScannerBarcode, StringComparison.OrdinalIgnoreCase)
            || _lastHandledScannerBarcode.Contains(barcode, StringComparison.OrdinalIgnoreCase)
            || barcode.Contains(_lastHandledScannerBarcode, StringComparison.OrdinalIgnoreCase);
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

            var endIndex = _lowerMachineReceiveBuffer.IndexOf("end", StringComparison.OrdinalIgnoreCase);
            if (endIndex < 0)
            {
                if (_lowerMachineReceiveBuffer.Length > 8192)
                {
                    AddLog("下位机接收缓存超过 8192 字符，已清空");
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
                if (_waitingForItemHeader && _protocolAdapter.IsItemPayload(completePayload))
                {
                    _waitingForItemHeader = false;
                    AddLog("已收到新的 Item1 表头，恢复接收本次测试数据");
                }

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
                _ = TriggerScannerAsync();
                AddLog("下位机触发扫码，等待扫码枪返回条码");
            }

            return;
        }

        if (_protocolAdapter.IsItemPayload(payload))
        {
            var snapshot = _testFlowService.LoadTestItems(payload);
            if (!snapshot.Message.StartsWith("测试项目加载完成", StringComparison.Ordinal))
            {
                ApplySnapshot(snapshot, countYield: false);
                AddLog(snapshot.Message);
                return;
            }

            SyncConfiguredTestItems(snapshot);
            ApplySnapshot(snapshot, countYield: false);
            AddLog("收到下位机测试项列表，已按协议适配器解析");
            return;
        }

        if (_protocolAdapter.IsResultPayload(payload))
        {
            _ = CompleteByPayloadAsync(payload, countYield: true);
        }
    }

    private async Task TriggerScannerAsync()
    {
        if (!_configuration.Communication.ScannerTriggerEnabled)
        {
            AddLog("通讯配置未启用自动扫码触发，未发送扫码枪触发命令");
            return;
        }

        if (_scannerConnection is null)
        {
            AddLog("扫码枪未连接，自动触发失败");
            return;
        }

        var command = DecodeCommand(_configuration.Communication.ScannerTriggerCommand);
        if (string.IsNullOrEmpty(command))
        {
            AddLog("扫码触发命令为空，已跳过");
            return;
        }

        await _scannerConnection.SendStringAsync(command);
        AddLog($"sendScanner:{FormatCommandForLog(command)}");
    }

    private async Task StopScannerAsync()
    {
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

    private async Task RequestTestItemsAsync()
    {
        if (!_configuration.Communication.TestItemRequestEnabled)
        {
            return;
        }

        if (_lowerMachineConnection is null
            || _configuration.Communication.LowerMachine.ConnectionType == ConnectionType.Disabled)
        {
            AddLog("下位机未连接，测试项请求命令已跳过");
            return;
        }

        var command = DecodeCommand(_configuration.Communication.TestItemRequestCommand);
        if (string.IsNullOrEmpty(command))
        {
            AddLog("测试项请求命令为空，已跳过");
            return;
        }

        await SendLowerMachineAsync(command);
        AddLog($"sendLowerCom:{FormatCommandForLog(command)}");
    }

    private async Task SendLowerMachineAsync(string data)
    {
        if (_lowerMachineConnection is null)
        {
            AddLog("下位机未连接，发送失败");
            return;
        }

        await _lowerMachineConnection.SendStringAsync(data);
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
            await SendLowerMachineAsync(command);
            AddLog($"sendLowerCom:{command.Trim()}");
        }

        _scanAheadStatus = ScanAheadStatus.None;
        ApplySnapshot(_testFlowService.StartTest(), countYield: false);
    }

    private async Task SendLowerMachineNgAsync()
    {
        await SendLowerMachineAsync(_protocolAdapter.LowerMachineNgCommand);
        AddLog($"sendLowerCom:{_protocolAdapter.LowerMachineNgCommand.Trim()}");
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
        _lastHandledScannerBarcode = string.Empty;
        _scanAheadStatus = ScanAheadStatus.None;
        var snapshot = _testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration));
        ApplySnapshot(snapshot, countYield: false);
        AddLog(snapshot.Message);
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

    private void ResetLowerMachineReceiveBuffer(string? message = null)
    {
        if (!string.IsNullOrEmpty(_lowerMachineReceiveBuffer) && !string.IsNullOrWhiteSpace(message))
        {
            AddLog(message);
        }

        _lowerMachineReceiveBuffer = string.Empty;
        CancelLowerMachinePayloadTimeout();
    }

    private void DiscardCurrentTestAfterLowerMachineReset()
    {
        ResetLowerMachineReceiveBuffer();
        _waitingForItemHeader = true;
        _lastHandledScannerBarcode = string.Empty;
        _scanAheadStatus = ScanAheadStatus.None;
        _pendingJlyRetestFailure = null;

        var snapshot = _testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration));
        ApplySnapshot(snapshot, countYield: false);
        AddLog("收到下位机 reset：当前测试数据已作废，不保存、不导出、不上传 MES；等待新的 Item1 表头");
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
            _lastHandledScannerBarcode = string.Empty;
            _scanAheadStatus = ScanAheadStatus.None;
            _pendingJlyRetestFailure = null;
            ApplySnapshot(_testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration)), countYield: false);
            AddLog($"下位机报文超过 {_configuration.Communication.LowerMachinePayloadTimeoutSeconds} 秒未收到 end 或 reset：当前测试数据已作废，不保存、不导出、不上传 MES；等待新的 Item1 表头");
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
            await SendLowerMachineAsync(command);
            AddLog($"sendLowerCom:{command.Trim()}");
        }

        ApplySnapshot(_testFlowService.StartTest(), countYield: false);
    }

    private async Task CompleteByPayloadAsync(string payload, bool countYield)
    {
        var snapshot = _testFlowService.CompleteTest(payload);
        ApplySnapshot(snapshot, countYield);
        AddLog(snapshot.Message);

        if (snapshot.IsPass.HasValue)
        {
            if (!await EnsureResultFilesAvailableAsync(snapshot.Barcode))
            {
                return;
            }

            RecordProductionResult(snapshot);
            PersistDailyYieldStatistics();
            await UpdateConsecutiveNgAlarmAsync(snapshot);
            await ExportResultAsync(snapshot);
            if (snapshot.IsPass == false && _configuration.User.Jly.ShowFailResultPopup)
            {
                await ShowRuntimePopupAsync("测试结果：FAIL", requiresConfirmation: false);
            }
            await UploadMesResultAsync(snapshot);
        }
    }

    private async Task ExportResultAsync(TestFlowSnapshot snapshot)
    {
        var results = await _exportRuntimeService.ExportAsync(_configuration, snapshot);

        foreach (var result in results)
        {
            AddLog(result.IsSuccess
                ? $"{result.ExporterName}:{result.Message} {result.FilePath}"
                : $"{result.ExporterName}:{result.Message}");
        }
    }

    private async Task<bool> EnsureResultFilesAvailableAsync(string barcode)
    {
        var lockedFiles = GetLockedResultFiles(barcode);
        if (lockedFiles.Count == 0)
        {
            return true;
        }

        var message = $"以下结果文件正在被占用或不可写入，请关闭文件后重新测试：{Environment.NewLine}"
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

        return candidateFiles
            .Where(File.Exists)
            .Where(IsFileLockedForWrite)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
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

    private async Task UploadMesResultAsync(TestFlowSnapshot snapshot)
    {
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
                    await UploadMesSnapshotAsync(_pendingJlyRetestFailure);
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

        await UploadMesSnapshotAsync(snapshot);
    }

    private async Task UploadMesSnapshotAsync(TestFlowSnapshot snapshot)
    {
        if (ShouldBlockMesUploadByYield())
        {
            AddLog($"JLY 测试片良率 {YieldRate:F2}% 小于或等于设定值 {_configuration.User.Jly.YieldUploadThreshold:F2}%，不上传 MES");
            return;
        }

        var result = await _mesRuntimeService.UploadResultAsync(_configuration, snapshot);
        if (string.Equals(result.Code, "DUPLICATE", StringComparison.OrdinalIgnoreCase))
        {
            AddLog($"MES上传跳过：{result.Message}");
            return;
        }

        var message = result.IsSuccess ? $"MES上传成功：{result.Message}" : $"MES上传失败：{result.Message}";
        AddLog(message);
        if (!result.IsSuccess)
        {
            _ = ShowRuntimePopupAsync(message, requiresConfirmation: false);
        }
    }

    private Task SimulatePopupAsync()
    {
        return ShowRuntimePopupAsync("模拟提醒：MES 上传失败，请检查网络连接或 MES 服务状态。", force: true);
    }

    private bool ShouldBlockMesUploadByYield()
    {
        if (!_configuration.User.Jly.TestPieceMode)
        {
            return false;
        }

        return YieldRate <= _configuration.User.Jly.YieldUploadThreshold;
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
        if (!_configuration.User.SaveDailyQuantity)
        {
            return;
        }

        var statistics = _configuration.User.DailyYieldStatistics ??= new DailyYieldStatistics();
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
        if (!_configuration.User.SaveDailyQuantity)
        {
            return;
        }

        var statistics = _configuration.User.DailyYieldStatistics ??= new DailyYieldStatistics();
        statistics.Date = DateTime.Today.ToString("yyyy-MM-dd");
        statistics.PassCount = PassCount;
        statistics.FailCount = FailCount;
        _configurationStore.Save(_configuration);
    }

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
        OnPropertyChanged(nameof(LoginDisplay));
        OnPropertyChanged(nameof(CanManageSystem));
        OnPropertyChanged(nameof(CanConfigureOperatorSettings));
        OnPropertyChanged(nameof(CanOpenBasicConfiguration));
        OnPropertyChanged(nameof(CanOpenCommunicationConfiguration));
        OnPropertyChanged(nameof(CanManageTestItems));
        OnPropertyChanged(nameof(CanManageUsers));
        OnPropertyChanged(nameof(CanImportProjectProfile));
        OnPropertyChanged(nameof(CanViewAnalysis));
        OnPropertyChanged(nameof(CanViewHelp));
        OnPropertyChanged(nameof(CanUseAdminTools));
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
        if ((!force && !_configuration.Popup.Enabled) || string.IsNullOrWhiteSpace(message))
        {
            return Task.CompletedTask;
        }

        var popup = new PopupNotification(message, requiresConfirmation ?? _configuration.Popup.RequireConfirmation);
        _popupQueue.Enqueue(popup);
        ShowNextPopup();
        return popup.Completion.Task;
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
            ConnectionType.Disabled => "已禁用",
            _ => configuration.ConnectionType.ToString()
        };
    }
}






