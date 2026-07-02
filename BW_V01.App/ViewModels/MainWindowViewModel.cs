using System.Collections.ObjectModel;
using System.Windows.Input;
using BW_V01.App.Commands;
using BW_V01.App.Services;
using BW_V01.Core.Communication;
using BW_V01.Core.Configuration;
using BW_V01.Core.Mes;
using BW_V01.Core.ResultExport;
using BW_V01.Core.TestFlow;

namespace BW_V01.App.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private const string SampleItemsPayload = "Item1,J1_1-SHORT,10,0,ohm,1,1;Item2,R1,11.000,9.000,Kohm,1,1;Item3,U1-ID,0x38,0x38,HEX,1,1;3;end";
    private const string SamplePassPayload = "Test1,5.000,OK;Test2,9.983,OK;Test3,0x38,OK;3;end";
    private const string SampleFailPayload = "Test1,5.000,OK;Test2,12.600,NG;Test3,0x38,OK;3;end";

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
    private bool _isPopupVisible;
    private bool _popupRequiresConfirmation;
    private int _passCount;
    private int _failCount;
    private int _currentConsecutiveNg;
    private int _popupVersion;
    private TaskCompletionSource? _popupConfirmation;

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

        OpenBasicConfigurationCommand = new RelayCommand(OpenBasicConfiguration);
        OpenCommunicationConfigurationCommand = new RelayCommand(OpenCommunicationConfiguration);
        OpenMesConfigurationCommand = new RelayCommand(OpenMesConfiguration);
        OpenTestItemConfigurationCommand = new RelayCommand(OpenTestItemConfiguration);
        ImportProjectProfileCommand = new RelayCommand(ImportProjectProfile);
        OpenUserConfigurationCommand = new RelayCommand(OpenUserConfiguration);
        SaveConfigurationCommand = new RelayCommand(Save);
        ConnectDevicesCommand = new AsyncRelayCommand(ConnectDevicesAsync);
        DisconnectDevicesCommand = new AsyncRelayCommand(DisconnectDevicesAsync);
        TestScannerCommand = new AsyncRelayCommand(TestScannerAsync);
        TestLowerMachineCommand = new AsyncRelayCommand(TestLowerMachineAsync);
        ToggleOnlineModeCommand = new AsyncRelayCommand(ToggleOnlineModeAsync);
        ClearYieldCommand = new RelayCommand(ClearYield);
        OpenDataAnalysisCommand = new RelayCommand(OpenDataAnalysis);
        InitializeFlowCommand = new RelayCommand(InitializeFlow);
        SimulateScanCommand = new RelayCommand(SimulateScan);
        SimulatePassCommand = new AsyncRelayCommand(() => CompleteByPayloadAsync(SamplePassPayload, true));
        SimulateFailCommand = new AsyncRelayCommand(() => CompleteByPayloadAsync(SampleFailPayload, true));
        SwitchLoginCommand = new RelayCommand(SwitchLogin);
        ConfirmPopupCommand = new RelayCommand(ConfirmPopup);
        HelpCommand = new RelayCommand(() => _configurationDialogService.ShowHelp(_configuration));

        TestItems = new ObservableCollection<TestItemViewModel>();
        Logs = new ObservableCollection<string>();
        ProductionRecords = new ObservableCollection<ProductionRecordViewModel>();
        RefreshDerivedState();
        ApplySnapshot(_testFlowService.LoadConfiguredTestItems(_configuration.TestItems.Items), countYield: false);
        AddLog("系统启动，配置已加载");
        AddLog($"协议适配器已加载：{_protocolAdapter.Name}({_protocolAdapter.Id})");
        AddLog(BuildProjectProfileLog());
        AddLog(BuildMesPluginLog());
        AddLog(BuildExportPluginLog());
    }

    public ICommand OpenBasicConfigurationCommand { get; }
    public ICommand OpenCommunicationConfigurationCommand { get; }
    public ICommand OpenMesConfigurationCommand { get; }
    public ICommand OpenTestItemConfigurationCommand { get; }
    public ICommand ImportProjectProfileCommand { get; }
    public ICommand OpenUserConfigurationCommand { get; }
    public ICommand SaveConfigurationCommand { get; }
    public ICommand ConnectDevicesCommand { get; }
    public ICommand DisconnectDevicesCommand { get; }
    public ICommand TestScannerCommand { get; }
    public ICommand TestLowerMachineCommand { get; }
    public ICommand ToggleOnlineModeCommand { get; }
    public ICommand ClearYieldCommand { get; }
    public ICommand OpenDataAnalysisCommand { get; }
    public ICommand InitializeFlowCommand { get; }
    public ICommand SimulateScanCommand { get; }
    public ICommand SimulatePassCommand { get; }
    public ICommand SimulateFailCommand { get; }
    public ICommand SwitchLoginCommand { get; }
    public ICommand ConfirmPopupCommand { get; }
    public ICommand HelpCommand { get; }

    public ObservableCollection<TestItemViewModel> TestItems { get; }
    public ObservableCollection<string> Logs { get; }
    public ObservableCollection<ProductionRecordViewModel> ProductionRecords { get; }

    public string WindowTitle => string.IsNullOrWhiteSpace(_configuration.WindowTitle) ? "BW_V01" : _configuration.WindowTitle;
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
    public string ScannerSummary => FormatDevice(_configuration.Communication.Scanner);
    public string LowerMachineSummary => FormatDevice(_configuration.Communication.LowerMachine);
    public string LoginDisplay => $"{_authenticationService.CurrentSession.DisplayName} / {_authenticationService.CurrentSession.RoleText}";
    public bool CanManageSystem => _authenticationService.CurrentSession.CanManageSystem;
    public bool CanManageUsers => _authenticationService.CurrentSession.CanManageUsers;
    public bool CanImportProjectProfile => _authenticationService.CurrentSession.CanImportProjectProfile;
    public bool CanViewAnalysis => _authenticationService.CurrentSession.CanViewAnalysis;
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
        private set => SetField(ref _scannerState, value);
    }

    public string LowerMachineState
    {
        get => _lowerMachineState;
        private set => SetField(ref _lowerMachineState, value);
    }

    public string MesState
    {
        get => _mesState;
        private set => SetField(ref _mesState, value);
    }

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

    public bool PopupRequiresConfirmation
    {
        get => _popupRequiresConfirmation;
        private set => SetField(ref _popupRequiresConfirmation, value);
    }

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
        if (!CanManageSystem)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改基础配置，请切换工程师或管理员账号。");
            return;
        }

        var editable = ConfigurationStore.Clone(_configuration);
        if (!_configurationDialogService.ShowBasicConfiguration(editable))
        {
            StatusMessage = "已取消基础配置";
            return;
        }

        _configuration = editable;
        Save();
        RefreshDerivedState();
        AddLog("基础配置已更新");
    }

    private void OpenCommunicationConfiguration()
    {
        if (!CanManageSystem)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改通讯设置，请切换工程师或管理员账号。");
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

    private void OpenTestItemConfiguration()
    {
        if (!CanManageSystem)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改测试项配置，请切换工程师或管理员账号。");
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

        var currentUser = _configuration.User;
        var imported = ConfigurationStore.Clone(profile.Configuration);
        imported.User = currentUser;
        imported.Mes.OnlineMode = false;
        imported.Compatibility.ProjectProfileId = profile.Id;
        imported.Compatibility.ProtocolAdapterId = profile.ProtocolAdapterId;

        imported.User.EnsureDefaultAccounts();
        imported.TestItems.EnsureDefaultItems();

        _configuration = imported;
        Save();
        RefreshDerivedState();
        ApplySnapshot(_testFlowService.LoadConfiguredTestItems(_configuration.TestItems.Items), countYield: false);
        AddLog($"已导入客户方案：{profile.Name}({profile.Id})，MES 已切换为离线模式");
    }

    private void OpenUserConfiguration()
    {
        if (!CanManageUsers)
        {
            _configurationDialogService.ShowAlert("权限不足", "当前账号不能修改用户配置，请切换工程师或管理员账号。");
            return;
        }

        var editable = ConfigurationStore.Clone(_configuration.User);
        if (!_configurationDialogService.ShowUserConfiguration(editable))
        {
            StatusMessage = "已取消用户配置";
            return;
        }

        _configuration.User = editable;
        Save();
        AddLog("用户配置已更新");
    }

    private void SwitchLogin()
    {
        ShowLogin();
    }

    public bool ShowLogin()
    {
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

    private async Task ConnectScannerAsync()
    {
        try
        {
            _scannerConnection = _deviceConnectionFactory.Create(_configuration.Communication.Scanner);
            _scannerConnection.DataReceived += ScannerConnection_DataReceived;
            await _scannerConnection.OpenAsync();
            ScannerState = _configuration.Communication.Scanner.ConnectionType == ConnectionType.Disabled ? "已禁用" : "已连接";
            AddLog($"扫码枪连接完成：{ScannerSummary}");
        }
        catch (Exception ex)
        {
            ScannerState = "异常";
            AddLog($"扫码枪连接失败：{ex.Message}");
        }
    }

    private async Task ConnectLowerMachineAsync()
    {
        try
        {
            _lowerMachineConnection = _deviceConnectionFactory.Create(_configuration.Communication.LowerMachine);
            _lowerMachineConnection.DataReceived += LowerMachineConnection_DataReceived;
            await _lowerMachineConnection.OpenAsync();
            LowerMachineState = _configuration.Communication.LowerMachine.ConnectionType == ConnectionType.Disabled ? "已禁用" : "已连接";
            AddLog($"下位机连接完成：{LowerMachineSummary}");
        }
        catch (Exception ex)
        {
            LowerMachineState = "异常";
            AddLog($"下位机连接失败：{ex.Message}");
        }
    }

    private async Task DisconnectDevicesAsync()
    {
        if (_scannerConnection is not null)
        {
            _scannerConnection.DataReceived -= ScannerConnection_DataReceived;
            await _scannerConnection.DisposeAsync();
            _scannerConnection = null;
        }

        if (_lowerMachineConnection is not null)
        {
            _lowerMachineConnection.DataReceived -= LowerMachineConnection_DataReceived;
            await _lowerMachineConnection.DisposeAsync();
            _lowerMachineConnection = null;
        }

        RefreshDerivedState();
        AddLog("设备连接已断开");
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
        PostToUi(() => _ = HandleScannerTextAsync(e.Text));
    }

    private void LowerMachineConnection_DataReceived(object? sender, DeviceDataReceivedEventArgs e)
    {
        PostToUi(() => HandleLowerMachineText(e.Text));
    }

    private async Task HandleScannerTextAsync(string text)
    {
        var barcode = NormalizeIncomingText(text);
        AddLog($"RecScanner:{barcode}");
        var snapshot = _testFlowService.AcceptBarcode(barcode, TestFlowOptions.FromConfiguration(_configuration));
        ApplySnapshot(snapshot, countYield: false);

        if (snapshot.State == TestFlowState.Ready)
        {
            if (_configuration.Mes.Enabled && _configuration.Mes.OnlineMode)
            {
                var mesResult = await _mesRuntimeService.CheckBeforeTestAsync(_configuration, barcode);
                AddLog(mesResult.IsSuccess ? $"MES卡关通过：{mesResult.Message}" : $"MES卡关失败：{mesResult.Message}");
                if (!mesResult.IsSuccess)
                {
                    await ShowRuntimePopupAsync($"MES卡关失败：{mesResult.Message}");
                    await SendLowerMachineAsync(_protocolAdapter.LowerMachineNgCommand);
                    AddLog($"sendLowerCom:{_protocolAdapter.LowerMachineNgCommand.Trim()}");
                    return;
                }
            }

            await SendLowerMachineAsync(_protocolAdapter.LowerMachineOkCommand);
            ApplySnapshot(_testFlowService.StartTest(), countYield: false);
            AddLog($"sendLowerCom:{_protocolAdapter.LowerMachineOkCommand.Trim()}");
        }
        else
        {
            await ShowRuntimePopupAsync(snapshot.Message);
            await SendLowerMachineAsync(_protocolAdapter.LowerMachineNgCommand);
            AddLog($"sendLowerCom:{_protocolAdapter.LowerMachineNgCommand.Trim()}");
        }
    }

    private void HandleLowerMachineText(string text)
    {
        var payload = NormalizeIncomingText(text);
        AddLog($"RecLower:{payload}");

        if (_protocolAdapter.IsIgnoredPayload(payload))
        {
            AddLog("协议适配器忽略下位机数据");
            return;
        }

        if (_protocolAdapter.IsStartSignal(payload))
        {
            InitializeFlow();
            if (!_configuration.User.AutoScan)
            {
                SimulateScan();
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
            ApplySnapshot(_testFlowService.LoadTestItems(payload), countYield: false);
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
    private async Task SendLowerMachineAsync(string data)
    {
        if (_lowerMachineConnection is null)
        {
            AddLog("下位机未连接，发送失败");
            return;
        }

        await _lowerMachineConnection.SendStringAsync(data);
    }

    private void InitializeFlow()
    {
        var snapshot = _testFlowService.Initialize(TestFlowOptions.FromConfiguration(_configuration));
        ApplySnapshot(snapshot, countYield: false);
        AddLog(snapshot.Message);
    }

    private void SimulateScan()
    {
        _ = HandleScannerTextAsync(BuildSampleBarcode());
    }

    private async Task CompleteByPayloadAsync(string payload, bool countYield)
    {
        var snapshot = _testFlowService.CompleteTest(payload);
        ApplySnapshot(snapshot, countYield);
        AddLog(snapshot.Message);

        if (snapshot.IsPass.HasValue)
        {
            await ShowRuntimePopupAsync(snapshot.Message);
            RecordProductionResult(snapshot);
            await UpdateConsecutiveNgAlarmAsync(snapshot);
            await ExportResultAsync(snapshot);
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

    private async Task UploadMesResultAsync(TestFlowSnapshot snapshot)
    {
        if (!_configuration.Mes.Enabled || !_configuration.Mes.OnlineMode)
        {
            return;
        }

        if (ShouldBlockMesUploadByYield(snapshot))
        {
            AddLog($"良率 {YieldRate:F2}% 低于设定值 {_configuration.Mes.YieldControlThreshold:F2}%，禁止上传 MES");
            return;
        }

        var result = await _mesRuntimeService.UploadResultAsync(_configuration, snapshot);
        AddLog(result.IsSuccess ? $"MES上传成功：{result.Message}" : $"MES上传失败：{result.Message}");
    }

    private bool ShouldBlockMesUploadByYield(TestFlowSnapshot snapshot)
    {
        if (!_configuration.Mes.EnableYieldControlUploadToMes || snapshot.IsPass == true)
        {
            return false;
        }

        return YieldRate < _configuration.Mes.YieldControlThreshold;
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
    private void ClearYield()
    {
        PassCount = 0;
        FailCount = 0;
        CurrentConsecutiveNg = 0;
        ProductionRecords.Clear();
        AddLog("良率统计和本次运行记录已清零");
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
        ScannerState = _configuration.Communication.Scanner.ConnectionType == ConnectionType.Disabled ? "已禁用" : "未连接";
        LowerMachineState = _configuration.Communication.LowerMachine.ConnectionType == ConnectionType.Disabled ? "已禁用" : "未连接";
        RaiseConfigurationProperties();
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
                Unit = item.Unit,
                Result = item.Result,
                LimitsChanged = UpdateTestItemLimits
            });
        }

        if (countYield && snapshot.IsPass.HasValue)
        {
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
        OnPropertyChanged(nameof(OnlineModeText));
        OnPropertyChanged(nameof(ScannerSummary));
        OnPropertyChanged(nameof(LowerMachineSummary));
        RaiseSessionProperties();
    }

    private void RaiseSessionProperties()
    {
        OnPropertyChanged(nameof(LoginDisplay));
        OnPropertyChanged(nameof(CanManageSystem));
        OnPropertyChanged(nameof(CanManageUsers));
        OnPropertyChanged(nameof(CanImportProjectProfile));
        OnPropertyChanged(nameof(CanViewAnalysis));
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

    private async Task ShowRuntimePopupAsync(string message)
    {
        if (!_configuration.Popup.Enabled || string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var version = Interlocked.Increment(ref _popupVersion);
        PopupMessage = message;
        PopupRequiresConfirmation = _configuration.Popup.RequireConfirmation;
        IsPopupVisible = true;

        if (!_configuration.Popup.RequireConfirmation)
        {
            _ = HidePopupAfterDelayAsync(version);
            return;
        }

        _popupConfirmation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await _popupConfirmation.Task;
    }

    private async Task HidePopupAfterDelayAsync(int version)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (version != _popupVersion || PopupRequiresConfirmation)
        {
            return;
        }

        IsPopupVisible = false;
    }

    private void ConfirmPopup()
    {
        IsPopupVisible = false;
        _popupConfirmation?.TrySetResult();
        _popupConfirmation = null;
    }

    private void AddLog(string message)
    {
        Logs.Insert(0, $"{DateTime.Now:HH:mm:ss}  {message}");
        while (Logs.Count > 200)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }
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
            return $"MES插件目录无可用插件：{_mesRuntimeService.PluginDirectory}";
        }

        return "MES插件已加载：" + string.Join(", ", _mesRuntimeService.Plugins.Select(plugin => $"{plugin.Name}({plugin.Id})"));
    }

    private string BuildExportPluginLog()
    {
        if (_exportRuntimeService.Plugins.Count == 0)
        {
            return $"导出插件目录无可用插件：{_exportRuntimeService.PluginDirectory}";
        }

        return "导出插件已加载：" + string.Join(", ", _exportRuntimeService.Plugins.Select(plugin => $"{plugin.Name}({plugin.Id})"));
    }

    private string BuildProjectProfileLog()
    {
        if (_projectProfiles.Count == 0)
        {
            return "项目Profile目录无可用模板";
        }

        return "项目Profile已加载：" + string.Join(", ", _projectProfiles.Select(profile => $"{profile.Name}({profile.Id})"));
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






