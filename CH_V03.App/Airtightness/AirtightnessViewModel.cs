using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CH_V03.App.Communication;
using CH_V03.App.Services;
using CH_V03.App.ViewModels;
using CH_V03.Core.Airtightness;
using CH_V03.Core.Communication;
using CH_V03.Core.Configuration;

namespace CH_V03.App.Airtightness;

public sealed class AirtightnessViewModel : ViewModelBase
{
    private const int MotionCommandMaxAttempts = 3;
    private readonly AirtightnessStore _store;
    private AirtightnessResultStore _results;
    private readonly RuntimeLogWriter _logWriter = new();
    private readonly DeviceConnectionFactory _deviceConnectionFactory = new(configuration => new UsbPosHidDeviceConnection(configuration));
    private IDeviceConnection? _scannerConnection;
    private ByzModbusRtuClient? _instrumentClient;
    private DigitalIoModbusRtuClient? _motionClient;
    private bool _motionTriggeredForBarcodePair;
    private CancellationTokenSource? _scannerWaitCancellation;
    private CancellationTokenSource? _instrumentTestCancellation;
    private Task? _instrumentTestTask;
    private bool _isDisconnectingInstrument;
    private bool _manualStopRequested;
    private bool _isInstrumentRecovering;
    private bool _continuousInstrumentMonitoringEnabled;
    private DateTime _lastDiagnosticLogAt = DateTime.MinValue;
    private bool _automaticScanRunning;
    public ObservableCollection<string> Logs { get; } = new();
    public event Action? TestCycleCompleted;
    public string LogDirectory => _store.GetLogDirectory(Configuration);
    public string RecordsDirectory => _store.GetRecordsDirectory(Configuration);
    public UserSession Session { get; }
    public AirtightnessConfiguration Configuration { get; private set; }
    public AirtightnessStatistics Statistics { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public string WindowTitle => $"CeHao · 气密检测 · {Configuration.ProductName}";
    public string LoginDisplay => $"{Session.DisplayName} / {Session.RoleText}";
    public bool CanConfigure => Session.CanManageSystem && !IsSimulating && !IsInstrumentTesting && !_isInstrumentRecovering;
    private CancellationTokenSource? _simulationCancellation;
    private int _simulationNumber;
    public bool IsSimulating { get; private set; }
    public bool IsInstrumentTesting { get; private set; }
    public bool CanSwitchAccount => !IsSimulating && !IsInstrumentTesting && !_isInstrumentRecovering;
    public bool CanClearCurrentStatistics => !IsSimulating && !IsInstrumentTesting && !_isInstrumentRecovering;
    public bool CanArmInstrumentTest => IsNoScanMode && IsInstrumentConnected && !IsSimulating && !IsInstrumentTesting && !_isInstrumentRecovering;
    public bool CanStopInstrumentMonitoring => IsInstrumentTesting || _continuousInstrumentMonitoringEnabled;
    public string ScannerState { get; private set; } = "未连接";
    public string InstrumentState { get; private set; } = "未连接";
    public string MotionState { get; private set; } = "未连接";
    public string ScannerStateBrush => ScannerState == "已连接" ? "#16A34A" : "#94A3B8";
    public string InstrumentStateBrush => InstrumentState == "已连接" ? "#16A34A" : "#94A3B8";
    public string MotionStateBrush => MotionState == "已连接" ? "#16A34A" : "#94A3B8";
    public bool IsScannerConnected => _scannerConnection?.IsOpen == true;
    public bool IsInstrumentConnected => _instrumentClient?.IsConnected == true;
    public bool IsMotionConnected => _motionClient?.IsConnected == true;
    public bool IsManualScanMode => Configuration.ScanMode == AirtightnessScanMode.Manual;
    public bool IsAutomaticScanMode => Configuration.ScanMode == AirtightnessScanMode.Automatic;
    public bool IsNoScanMode => Configuration.ScanMode == AirtightnessScanMode.NoScan;
    public string ScanModeText => Configuration.ScanMode switch
    {
        AirtightnessScanMode.Manual => "手动扫码 · CH1 扫完自动切换 CH2",
        AirtightnessScanMode.Automatic => "自动扫码 · 扫码枪按 CH1、CH2 顺序接收",
        _ => "不扫码 · 可直接准备测试"
    };
    public string Channel1Barcode { get; private set; } = string.Empty;
    public string Channel2Barcode { get; private set; } = string.Empty;
    public bool IsBarcodePairReady => IsNoScanMode || (!string.IsNullOrWhiteSpace(Channel1Barcode) && !string.IsNullOrWhiteSpace(Channel2Barcode));
    public string SimulationButtonText => IsSimulating ? "停止模拟" : "模拟测试";
    public string SimulationSummary { get; private set; } = "请先连接气密仪；扫码完成后可准备双通道测试。";
    public string Channel1State { get; private set; } = "WAIT";
    public string Channel2State { get; private set; } = "WAIT";
    public string Channel1ResultForeground => ResultForeground(Channel1State);
    public string Channel2ResultForeground => ResultForeground(Channel2State);
    public string Channel1ResultBackground => ResultBackground(Channel1State);
    public string Channel2ResultBackground => ResultBackground(Channel2State);
    public string Channel1ResultBorderBrush => ResultBorderBrush(Channel1State);
    public string Channel2ResultBorderBrush => ResultBorderBrush(Channel2State);
    public string Pressure1Text { get; private set; } = "—";
    public string Pressure2Text { get; private set; } = "—";
    public string Leak1Text { get; private set; } = "—";
    public string Leak2Text { get; private set; } = "—";
    public string InstrumentResult1Text { get; private set; } = "—";
    public string InstrumentResult2Text { get; private set; } = "—";
    public string DiagnosticChannel1 { get; private set; } = "等待连接气密仪。";
    public string DiagnosticChannel2 { get; private set; } = "等待连接气密仪。";
    public string DiagnosticUpdatedText { get; private set; } = "尚未读取";
    public string DiagnosticErrorText { get; private set; } = "无";
    public double WaveformDuration => Math.Max(20d, AirtightnessSimulation.Duration(Configuration));
    public double WaveformMaximum => (double)Configuration.MaximumPressureKpa * 1.15;
    public double WaveformFillSeconds => (double)Configuration.FillSeconds;
    public double WaveformBalanceSeconds => (double)Configuration.BalanceSeconds;
    public double WaveformTestSeconds => (double)Configuration.TestSeconds;
    public double WaveformExhaustSeconds => (double)Configuration.ExhaustSeconds;
    public ObservableCollection<PressureSample> PressureSamples { get; } = new();
    public ObservableCollection<AirtightnessHistoryRow> SimulationHistory { get; } = new();
    public string CountingModeText => Configuration.CountingMode == AirtightnessCountingMode.SeparateProducts
        ? "双产品模式 · 每个通道独立计 1 件" : "单产品模式 · 两通道共同判定 1 件";
    public string CountingNote => Configuration.StatisticsStartAt is { } start
        ? $"统计起点：{start.LocalDateTime:yyyy-MM-dd HH:mm:ss}；此前正式记录已保留，但不计入当前 PASS/FAIL。"
        : "统计当前保存目录内的记录，两种模式分别累计；旧目录文件保留。";
    public string PressureRange => $"{Configuration.MinimumPressureKpa:0.##}–{Configuration.MaximumPressureKpa:0.##} kPa";
    public string FillTime => $"{Configuration.FillSeconds:0.##} s";
    public string BalanceTime => $"{Configuration.BalanceSeconds:0.##} s";
    public string TestTime => $"{Configuration.TestSeconds:0.##} s";
    public string ExhaustTime => $"{Configuration.ExhaustSeconds:0.##} s";
    public string LeakLimit => $"≤ {Configuration.MaximumLeakPa:0.##} Pa";
    public string YieldText => $"{Statistics.Yield:0.00}%";
    public string Channel1Summary => $"累计 PASS {Statistics.Channel1Pass}    FAIL {Statistics.Channel1Fail}";
    public string Channel2Summary => $"累计 PASS {Statistics.Channel2Pass}    FAIL {Statistics.Channel2Fail}";
    public ObservableCollection<AirtightnessHistoryRow> History { get; } = new();

    private static string ResultForeground(string state) => state.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ? "#B91C1C" : state.Contains("PASS", StringComparison.OrdinalIgnoreCase) ? "#15803D" : "#0284C7";
    private static string ResultBackground(string state) => state.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ? "#FEE2E2" : state.Contains("PASS", StringComparison.OrdinalIgnoreCase) ? "#DCFCE7" : "#EFF6FF";
    private static string ResultBorderBrush(string state) => state.Contains("FAIL", StringComparison.OrdinalIgnoreCase) ? "#EF4444" : state.Contains("PASS", StringComparison.OrdinalIgnoreCase) ? "#22C55E" : "#7DD3FC";
    public AirtightnessViewModel(UserSession session, AirtightnessStore store)
    {
        if (!session.IsAuthenticated || session.Module != TestModule.Airtightness)
            throw new UnauthorizedAccessException("只有气密账号可以打开气密工作站。");
        Session = session;
        _store = store;
        Configuration = store.LoadConfiguration();
        _results = new AirtightnessResultStore(RecordsDirectory);
        AddLog($"气密工作站启动，用户：{session.DisplayName}（{session.UserName}）；{CountingModeText}。");
        AddLog("气密仪未连接。协议已按 BYZ Modbus RTU（地址 01、9600、8N1）配置。", "提示");
        Refresh();
    }

    public void SaveConfiguration(AirtightnessConfiguration edited)
    {
        if (!CanConfigure) throw new UnauthorizedAccessException("当前账号无权修改气密配置。");
        edited.Validate();
        var results = new AirtightnessResultStore(_store.GetRecordsDirectory(edited));
        var records = results.LoadAll();
        _store.SaveConfiguration(edited);
        Configuration = ConfigurationStore.Clone(edited);
        _results = results;
        PressureSamples.Clear();
        Channel1State = Channel2State = "WAIT";
        Pressure1Text = Pressure2Text = Leak1Text = Leak2Text = "—";
        InstrumentResult1Text = InstrumentResult2Text = "—";
        SimulationSummary = "配置已更新并已由气密仪回读确认；可使用模拟测试预览双通道压力变化。";
        AddLog($"气密配置已保存：产品 {Configuration.ProductName}；{CountingModeText}；气压 {PressureRange}；充气 {FillTime}，平衡 {BalanceTime}，测试 {TestTime}，泄气 {ExhaustTime}；压降 {LeakLimit}。");
        AddLog($"保存路径：测试记录 {RecordsDirectory}；日志 {LogDirectory}。");
        Refresh(records);

    }

    /// <summary>保存产品、统计、扫码、路径与下位机策略等本地基础设置；不向气密仪下发配方。</summary>
    public void SaveBasicConfiguration(AirtightnessConfiguration edited)
    {
        if (!CanConfigure) throw new UnauthorizedAccessException("当前账号无权修改基础配置。");
        edited.Validate();
        var results = new AirtightnessResultStore(_store.GetRecordsDirectory(edited));
        var records = results.LoadAll();
        _store.SaveConfiguration(edited);
        Configuration = ConfigurationStore.Clone(edited);
        _results = results;
        Refresh(records);
        AddLog($"基础配置已保存：产品 {Configuration.ProductName}；{CountingModeText}；{ScanModeText}；测试记录 {RecordsDirectory}；日志 {LogDirectory}；下位机策略{(Configuration.Motion.Enabled ? "允许上位机控制继电器" : "手动测试跳过下位机控制")}。", "信息");
        RaiseAllPropertiesChanged();
    }

    /// <summary>从当前时刻重新开始生产统计，不删除 JSON、CSV 等正式追溯文件。</summary>
    public void ClearCurrentStatistics()
    {
        if (!CanClearCurrentStatistics)
            throw new InvalidOperationException("测试、模拟或设备恢复期间不能清除当前统计。");

        var clearedPass = Statistics.Pass;
        var clearedFail = Statistics.Fail;
        var edited = ConfigurationStore.Clone(Configuration);
        edited.StatisticsStartAt = DateTimeOffset.Now;
        _store.SaveConfiguration(edited);
        Configuration = edited;
        Statistics = new AirtightnessStatistics(0, 0, 0, 0, 0, 0);
        History.Clear();
        SimulationHistory.Clear();
        AddLog($"已清除当前生产统计：PASS {clearedPass}，FAIL {clearedFail}。正式 JSON、CSV 和此前测试记录未删除；新统计从 {edited.StatisticsStartAt.Value.LocalDateTime:yyyy-MM-dd HH:mm:ss} 开始。", "提示");
        RaiseAllPropertiesChanged();
    }
    public async Task SaveConfigurationToInstrumentAsync(AirtightnessConfiguration edited)
    {
        if (!CanConfigure) throw new UnauthorizedAccessException("当前账号无权修改气密配置。");
        edited.Validate();
        if (!IsInstrumentConnected || _instrumentClient is null)
            throw new InvalidOperationException("请先通过“设备连接”连接气密仪，再保存并同步配置。");

        AddLog("开始向气密仪写入配方参数，并将在写入后回读确认。", "提示");
        await _instrumentClient.WriteRecipeAsync(edited);
        await _instrumentClient.VerifyRecipeAsync(edited);
        SaveConfiguration(edited);
        AddLog("气密仪参数已写入并逐项回读确认。", "信息");
    }

    public void SaveDeviceConfiguration(DeviceConnectionConfiguration scanner, AirtightnessInstrumentConfiguration instrument,
        AirtightnessMotionConfiguration motion, bool scannerTriggerEnabled, string? scannerTriggerCommand,
        string? scannerStopCommand, int scannerWaitTimeoutSeconds)
    {
        if (!CanConfigure) throw new UnauthorizedAccessException("当前账号无权修改设备连接配置。");
        scanner.Name = "气密扫码枪";
        instrument.Validate();
        motion.Validate();
        var edited = ConfigurationStore.Clone(Configuration);
        edited.Scanner = ConfigurationStore.Clone(scanner);
        edited.Instrument = ConfigurationStore.Clone(instrument);
        edited.Motion = ConfigurationStore.Clone(motion);
        edited.ScannerTriggerEnabled = scannerTriggerEnabled;
        edited.ScannerTriggerCommand = scannerTriggerCommand?.Trim() ?? string.Empty;
        edited.ScannerStopCommand = scannerStopCommand?.Trim() ?? string.Empty;
        edited.ScannerWaitTimeoutSeconds = scannerWaitTimeoutSeconds;
        _store.SaveConfiguration(edited);
        Configuration = edited;
        AddLog($"设备连接配置已保存：扫码枪 {Configuration.Scanner.ConnectionType}，自动触发{(Configuration.ScannerTriggerEnabled ? "已启用" : "未启用")}，等待 {Configuration.ScannerWaitTimeoutSeconds} 秒；气密仪 {instrument.PortName} / {instrument.BaudRate} / 站号 {instrument.StationAddress}；下位机 {motion.PortName} / {motion.BaudRate} / 站号 {motion.StationAddress}，启动=X{motion.StartInput}，CH1=Y{motion.Channel1Output}，CH2=Y{motion.Channel2Output}，结果后保持 {motion.OutputReleaseDelaySeconds:0.###} 秒，输出控制{(motion.Enabled ? "已允许" : "未启用")}，手动跳过继电器{(motion.SkipRelayControlForManualTest ? "已启用" : "未启用")}。", "信息");
        RaiseAllPropertiesChanged();
    }

    /// <summary>
    /// 仅更新上位机的记录与日志保存位置，不修改也不向气密仪下发测试参数。
    /// 用于设备尚未连接时先修复或迁移本地保存路径。
    /// </summary>
    public void SaveStorageDirectories(string? recordsDirectory, string? logDirectory)
    {
        if (!CanConfigure) throw new UnauthorizedAccessException("当前账号无权修改气密配置。");

        var edited = ConfigurationStore.Clone(Configuration);
        edited.RecordsDirectory = recordsDirectory?.Trim() ?? string.Empty;
        edited.LogDirectory = logDirectory?.Trim() ?? string.Empty;
        edited.Validate();

        var results = new AirtightnessResultStore(_store.GetRecordsDirectory(edited));
        _store.SaveConfiguration(edited);
        Configuration = edited;
        _results = results;
        Refresh(results.LoadAll());
        AddLog($"本地保存路径已更新：测试记录 {RecordsDirectory}；日志 {LogDirectory}。", "信息");
        RaiseAllPropertiesChanged();
    }

    public void SaveMotionConfiguration(AirtightnessMotionConfiguration motion)
    {
        if (!CanConfigure) throw new UnauthorizedAccessException("当前账号无权修改动作控制配置。");
        motion.Validate();
        var edited = ConfigurationStore.Clone(Configuration);
        edited.Motion = ConfigurationStore.Clone(motion);
        _store.SaveConfiguration(edited);
        Configuration = edited;
        AddLog($"双通道下位机设置已保存：{motion.PortName} / {motion.BaudRate} / 站号 {motion.StationAddress}；启动=X{motion.StartInput}，CH1=Y{motion.Channel1Output}，CH2=Y{motion.Channel2Output}，结果后保持 {motion.OutputReleaseDelaySeconds:0.###} 秒；输出控制{(motion.Enabled ? "已允许" : "未启用")}；手动跳过继电器{(motion.SkipRelayControlForManualTest ? "已启用" : "未启用")}。", "信息");
        RaiseAllPropertiesChanged();
    }

    public async Task ToggleScannerConnectionAsync()
    {
        if (IsScannerConnected)
        {
            CancelScannerWait();
            _automaticScanRunning = false;
            await _scannerConnection!.CloseAsync();
            await _scannerConnection.DisposeAsync();
            _scannerConnection = null;
            ScannerState = "未连接";
            AddLog("扫码枪已断开。");
            RaiseAllPropertiesChanged();
            return;
        }

        if (Configuration.Scanner.ConnectionType == ConnectionType.Disabled)
            throw new InvalidOperationException("扫码枪通讯方式当前为“禁用”，请先在设备连接中选择实际方式。");
        var scanner = _deviceConnectionFactory.Create(Configuration.Scanner);
        scanner.DataReceived += Scanner_DataReceived;
        try
        {
            await scanner.OpenAsync();
            _scannerConnection = scanner;
            ScannerState = "已连接";
            AddLog($"扫码枪已连接：{Configuration.Scanner.ConnectionType}。", "信息");
            if (IsAutomaticScanMode)
                await BeginAutomaticScanAsync();
        }
        catch
        {
            scanner.DataReceived -= Scanner_DataReceived;
            await scanner.DisposeAsync();
            throw;
        }
        finally { RaiseAllPropertiesChanged(); }
    }

    /// <summary>自动扫码模式在扫码枪连接后即开始依次接收 CH1、CH2；不向气密仪下发启动命令。</summary>
    private async Task BeginAutomaticScanAsync()
    {
        if (IsSimulating) throw new InvalidOperationException("模拟测试正在进行，请先停止模拟测试。");
        if (!IsScannerConnected || _scannerConnection is null)
            throw new InvalidOperationException("自动扫码前请先在“设备连接”中连接扫码枪。");

        ClearBarcodes();
        _automaticScanRunning = true;
        StartScannerWait();
        SimulationSummary = "已开始自动扫码，正在等待 CH1 条码。";
        AddLog("自动扫码模式已就绪，正在等待 CH1 条码。", "提示");
        RaiseAllPropertiesChanged();
        await TriggerScannerAsync(1);
    }

    public async Task ToggleInstrumentConnectionAsync()
    {
        if (_isInstrumentRecovering)
            throw new InvalidOperationException("气密仪正在停止或恢复连接，请稍候再操作。");

        // Do not use IsInstrumentConnected here. A cancelled read deliberately closes the port
        // before its monitoring task has finished; the client instance still owns that lifecycle.
        if (_instrumentClient is not null)
        {
            _isDisconnectingInstrument = true;
            try
            {
                _continuousInstrumentMonitoringEnabled = false;
                _instrumentTestCancellation?.Cancel();
                _instrumentClient.AbortPendingExchange();
                var monitoringTask = _instrumentTestTask;
                if (monitoringTask is not null) await monitoringTask;

                var existingInstrument = _instrumentClient;
                _instrumentClient = null;
                if (existingInstrument is not null)
                {
                    await existingInstrument.DisconnectAsync();
                    await existingInstrument.DisposeAsync();
                }
                InstrumentState = "未连接";
                SimulationSummary = "气密仪已断开。";
                AddLog("气密仪已断开。", "信息");
            }
            finally
            {
                _isDisconnectingInstrument = false;
                RaiseAllPropertiesChanged();
            }
            return;
        }

        var instrument = new ByzModbusRtuClient(Configuration.Instrument);
        try
        {
            await instrument.ConnectAsync();
            // A harmless read of current pressure confirms both serial settings and Modbus station/CRC.
            _ = await instrument.ReadFloatAsync(42);
            _instrumentClient = instrument;
            InstrumentState = "已连接";
            AddLog($"气密仪已连接：{Configuration.Instrument.PortName} / {Configuration.Instrument.BaudRate} / 站号 {Configuration.Instrument.StationAddress}。", "信息");
            if (IsNoScanMode)
            {
                SimulationSummary = "气密仪已连接。请先完成气密配置；准备开始时点击“准备等待面板 Start”。";
                AddLog("气密仪已连接：当前为不扫码模式，未自动开始等待；请在完成配置后手动准备等待。", "提示");
            }
        }
        catch
        {
            await instrument.DisposeAsync();
            throw;
        }
        finally { RaiseAllPropertiesChanged(); }
    }
    public async Task ToggleMotionConnectionAsync()
    {
        if (IsInstrumentTesting)
            throw new InvalidOperationException("本轮测试或动作模块恢复期间不能手动断开下位机。");
        if (_motionClient is not null)
        {
            await _motionClient!.DisconnectAsync();
            await _motionClient.DisposeAsync();
            _motionClient = null;
            MotionState = "未连接";
            AddLog("双通道下位机已断开。");
            RaiseAllPropertiesChanged();
            return;
        }
        var motion = new DigitalIoModbusRtuClient(Configuration.Motion);
        try
        {
            await motion.ConnectAsync();
            _ = await motion.ReadInputAsync(Configuration.Motion.StartInput);
            _motionClient = motion;
            MotionState = "已连接";
            AddLog($"双通道下位机已连接：{Configuration.Motion.PortName} / {Configuration.Motion.BaudRate} / 站号 {Configuration.Motion.StationAddress}。", "信息");
        }
        catch
        {
            await motion.DisposeAsync();
            throw;
        }
        finally { RaiseAllPropertiesChanged(); }
    }


    /// <summary>只验证扫码枪连接可打开；测试结束后立即释放，不触发扫码也不发送任何指令。</summary>
    public async Task TestScannerConnectionAsync()
    {
        if (Configuration.Scanner.ConnectionType == ConnectionType.Disabled)
            throw new InvalidOperationException("扫码枪通讯方式当前为“禁用”，请先选择实际方式。");
        if (IsScannerConnected)
        {
            AddLog("扫码枪测试连通成功：当前已连接。", "信息");
            return;
        }

        var scanner = _deviceConnectionFactory.Create(Configuration.Scanner);
        try
        {
            await scanner.OpenAsync();
            AddLog($"扫码枪测试连通成功：{Configuration.Scanner.ConnectionType} 可正常打开。", "信息");
        }
        finally
        {
            if (scanner.IsOpen) await scanner.CloseAsync();
            await scanner.DisposeAsync();
        }
    }

    /// <summary>只读 CH1 当前压力寄存器，验证气密仪的串口、Modbus 站号、CRC 和浮点字序。</summary>
    public async Task<float> TestInstrumentConnectionAsync()
    {
        if (_instrumentClient is not null && IsInstrumentConnected)
        {
            var pressure = await _instrumentClient.ReadChannelPressureAsync(1);
            AddLog($"气密仪测试连通成功：CH1 当前压力 {pressure:0.000} kPa。", "信息");
            return pressure;
        }

        await using var instrument = new ByzModbusRtuClient(Configuration.Instrument);
        await instrument.ConnectAsync();
        var currentPressure = await instrument.ReadChannelPressureAsync(1);
        AddLog($"气密仪测试连通成功：CH1 当前压力 {currentPressure:0.000} kPa。", "信息");
        return currentPressure;
    }

    /// <summary>只读取启动输入，不查询或写入任何 Y 输出。</summary>
    public async Task<bool> TestMotionConnectionAsync()
    {
        if (_motionClient is not null && IsMotionConnected)
        {
            var input = await _motionClient.ReadInputAsync(Configuration.Motion.StartInput);
            AddLog($"下位机测试连通成功：X{Configuration.Motion.StartInput}={(input ? "触发" : "未触发")}。", "信息");
            return input;
        }

        await using var motion = new DigitalIoModbusRtuClient(Configuration.Motion);
        await motion.ConnectAsync();
        var startInput = await motion.ReadInputAsync(Configuration.Motion.StartInput);
        AddLog($"下位机测试连通成功：X{Configuration.Motion.StartInput}={(startInput ? "触发" : "未触发")}。", "信息");
        return startInput;
    }
    public async Task SetMotionOutputAsync(int channel, bool enabled)

    {
        if (!CanConfigure) throw new UnauthorizedAccessException("当前账号无权控制气密动作继电器。");
        if (!Configuration.Motion.Enabled) throw new InvalidOperationException("下位机控制未启用。请先在下位机设置中确认接线并勾选“允许上位机控制下位机继电器”。");
        if (!IsMotionConnected || _motionClient is null) throw new InvalidOperationException("下位机未连接。");
        var output = channel == 1 ? Configuration.Motion.Channel1Output : channel == 2 ? Configuration.Motion.Channel2Output
            : throw new ArgumentOutOfRangeException(nameof(channel));
        await _motionClient.SetOutputAsync(output, enabled);
        AddLog($"CH{channel} 动作继电器 Y{output} 已{(enabled ? "开启" : "关闭")}。", "信息");
    }

    /// <returns>0=未接受，1=请将焦点切至 CH2，2=两码就绪。</returns>
    public int AcceptBarcode(int channel, string? text, bool automatic = false)
    {
        if (IsNoScanMode) return 0;
        var barcode = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(barcode)) return 0;
        if (barcode.Length > 128) throw new InvalidDataException("条码长度不能超过 128 个字符。");
        if (channel == 1)
        {
            Channel1Barcode = barcode;
            _motionTriggeredForBarcodePair = false;
            AddLog($"CH1 条码已接收：{barcode}。", automatic ? "信息" : "信息");
            if (automatic) SimulationSummary = "CH1 条码已收到，正在触发并等待 CH2 条码。";
            RaiseAllPropertiesChanged();
            return 1;
        }
        Channel2Barcode = barcode;
        AddLog($"CH2 条码已接收：{barcode}。", automatic ? "信息" : "信息");
        AddLog($"双通道条码已就绪：CH1 {Channel1Barcode}，CH2 {Channel2Barcode}。", "信息");
        CancelScannerWait();
        _automaticScanRunning = false;
        SimulationSummary = "双通道条码已就绪；正在按测试流程开启 I/O 并等待仪器面板 Start。";
        _ = StopScannerAsync();
        if (!_motionTriggeredForBarcodePair)
        {
            _motionTriggeredForBarcodePair = true;
            _ = OpenMotionAfterBarcodeAsync();
        }
        RaiseAllPropertiesChanged();
        return 2;
    }

    public void ClearBarcodes()
    {
        Channel1Barcode = Channel2Barcode = string.Empty;
        _motionTriggeredForBarcodePair = false;
        RaiseAllPropertiesChanged();
    }

    private async Task OpenMotionAfterBarcodeAsync()
    {
        try
        {
            await OpenMotionAsync("扫码完成");
        }
        catch (Exception exception)
        {
            AddLog($"扫码后自动开启动作继电器失败：{exception.Message}", "错误");
        }
    }

    private async Task OpenMotionAsync(string source)
    {
        if (ShouldSkipRelayControlForManualTest())
        {
            if (!IsInstrumentConnected || _instrumentClient is null)
                throw new InvalidOperationException("气密仪未连接，无法等待仪器面板 Start 并采集测试数据。");

            AddLog($"{source}：手动测试已跳过 CH1/CH2 I/O 继电器控制，等待操作员按下气密仪面板 Start。", "提示");
            await ArmManualInstrumentTestAsync();
            return;
        }

        if (!IsInstrumentConnected || _instrumentClient is null)
            throw new InvalidOperationException("气密仪未连接，无法等待仪器面板 Start 并采集测试数据。");

        if (!Configuration.Motion.Enabled || !IsMotionConnected || _motionClient is null)
        {
            var reason = !Configuration.Motion.Enabled ? "未启用下位机输出控制" : "下位机未连接";
            AddLog($"{source}：{reason}，不发送继电器命令，直接等待操作员按下气密仪面板 Start。", "提示");
            await ArmManualInstrumentTestAsync();
            return;
        }

        await ArmMotionInputTestAsync(source);
    }

    private bool ShouldSkipRelayControlForManualTest() =>
        Configuration.Motion.SkipRelayControlForManualTest && !IsAutomaticScanMode;

    private Task ArmMotionInputTestAsync(string source)
    {
        if (_instrumentClient is null || !IsInstrumentConnected) throw new InvalidOperationException("气密仪未连接。");
        if (_motionClient is null || !IsMotionConnected) throw new InvalidOperationException("下位机未连接。");
        if (IsInstrumentTesting) throw new InvalidOperationException("双通道气密测试正在进行。");

        var recipe = ConfigurationStore.Clone(Configuration);
        var cancellation = new CancellationTokenSource();
        _instrumentTestCancellation = cancellation;
        IsInstrumentTesting = true;
        PressureSamples.Clear();
        Channel1State = Channel2State = $"等待 X{recipe.Motion.StartInput} 触发";
        SimulationSummary = $"{source}：已就绪，等待 下位机 X{recipe.Motion.StartInput} 从未触发变为触发；收到后闭合 Y{recipe.Motion.Channel1Output}、Y{recipe.Motion.Channel2Output} 并开始本次测试。";
        AddLog($"{source}：等待 X{recipe.Motion.StartInput} 上升沿；收到信号后将闭合 Y{recipe.Motion.Channel1Output}、Y{recipe.Motion.Channel2Output} 并开始本次测试。", "提示");
        RaiseAllPropertiesChanged();
        _instrumentTestTask = WaitForMotionInputStartAsync(_instrumentClient, _motionClient, recipe, Guid.NewGuid(), cancellation);
        return Task.CompletedTask;
    }

    private async Task WaitForMotionInputStartAsync(ByzModbusRtuClient instrument, DigitalIoModbusRtuClient motion,
        AirtightnessConfiguration recipe, Guid cycleId, CancellationTokenSource cancellation)
    {
        var monitoringStarted = false;
        try
        {
            var (previous, _) = await ExecuteMotionWithRecoveryAsync(motion,
                () => motion.ReadInputAsync(recipe.Motion.StartInput, cancellation.Token),
                $"读取 X{recipe.Motion.StartInput}", cancellation.Token);
            if (previous) AddLog($"X{recipe.Motion.StartInput} 当前已触发；为防止上一轮残留信号重复启动，将等待其先复位后再触发。", "提示");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < recipe.ManualStartWaitTimeoutSeconds)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var (current, recovered) = await ExecuteMotionWithRecoveryAsync(motion,
                    () => motion.ReadInputAsync(recipe.Motion.StartInput, cancellation.Token),
                    $"读取 X{recipe.Motion.StartInput}", cancellation.Token);
                if (recovered)
                {
                    // An edge that occurred while COM4 was unavailable cannot be verified.
                    previous = current;
                    if (current)
                        AddLog($"{recipe.Motion.PortName} 恢复时 X{recipe.Motion.StartInput} 已触发；等待其先复位，再接收新的上升沿。", "提示");
                    else
                        AddLog($"{recipe.Motion.PortName} 已恢复，X{recipe.Motion.StartInput} 未触发，继续等待新的上升沿。", "信息");
                }
                if (timer.Elapsed.TotalSeconds >= recipe.ManualStartWaitTimeoutSeconds) break;
                if (!previous && current)
                {
                    await ExecuteMotionWithRecoveryAsync(motion,
                        async () => { await motion.SetOutputAsync(recipe.Motion.Channel1Output, true, cancellation.Token); return true; },
                        $"开启 Y{recipe.Motion.Channel1Output}", cancellation.Token);
                    await ExecuteMotionWithRecoveryAsync(motion,
                        async () => { await motion.SetOutputAsync(recipe.Motion.Channel2Output, true, cancellation.Token); return true; },
                        $"开启 Y{recipe.Motion.Channel2Output}", cancellation.Token);
                    monitoringStarted = true;
                    AddLog($"X{recipe.Motion.StartInput} 已触发：已闭合 Y{recipe.Motion.Channel1Output}、Y{recipe.Motion.Channel2Output}，开始本次双通道测试。", "信息");
                    await MonitorInstrumentTestAsync(instrument, recipe, DateTimeOffset.Now, cycleId, cancellation);
                    return;
                }

                previous = current;
                var seconds = timer.Elapsed.TotalSeconds;
                Channel1State = Channel2State = $"等待 X{recipe.Motion.StartInput} 触发";
                SimulationSummary = $"下位机已就绪，等待 X{recipe.Motion.StartInput} 触发（{seconds:0} / {recipe.ManualStartWaitTimeoutSeconds} 秒）。";
                RaiseAllPropertiesChanged();
                await Task.Delay(200, cancellation.Token);
            }

            Channel1State = Channel2State = "等待启动超时";
            SimulationSummary = $"未收到 X{recipe.Motion.StartInput} 启动信号，未保存测试记录和统计。";
            AddLog($"等待 X{recipe.Motion.StartInput} 启动信号超时（{recipe.ManualStartWaitTimeoutSeconds} 秒），未保存测试记录和统计。", "提示");
        }
        catch (OperationCanceledException)
        {
            Channel1State = Channel2State = "已停止";
            SimulationSummary = "等待 I/O 启动信号已取消，未保存测试记录和统计。";
            AddLog("等待 I/O 启动信号已取消。", "提示");
        }
        catch (Exception exception)
        {
            if (cancellation.IsCancellationRequested) return;
            Channel1State = Channel2State = "通讯异常";
            SimulationSummary = "等待 I/O 启动信号时通讯异常，未保存测试记录和统计。";
            SetDiagnosticError(exception.Message);
            AddLog($"等待 X{recipe.Motion.StartInput} 启动信号时通讯异常：{exception.Message}", "错误");
        }
        finally
        {
            if (!monitoringStarted)
            {
                await CloseMotionOutputsAsync();
                FinishInstrumentTest(cancellation, resumeNoScanStandby: true);
            }
        }
    }

    private async Task<(T Value, bool Recovered)> ExecuteMotionWithRecoveryAsync<T>(
        DigitalIoModbusRtuClient motion, Func<Task<T>> command, string description, CancellationToken cancellationToken = default)
    {
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= MotionCommandMaxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (attempt > 1) await motion.ReconnectAsync(cancellationToken);
                var value = await command();
                if (attempt > 1)
                {
                    MotionState = "已连接";
                    AddLog($"动作模块重连成功：{description} 第 {attempt}/{MotionCommandMaxAttempts} 次发送收到匹配且 CRC 正确的回复。", "信息");
                    RaiseAllPropertiesChanged();
                }
                return (value, attempt > 1);
            }
            catch (Exception exception) when (exception is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                lastFailure = exception;
                MotionState = attempt < MotionCommandMaxAttempts ? "等待重连" : "通讯异常";
                if (IsInstrumentTesting)
                    SimulationSummary = attempt < MotionCommandMaxAttempts
                        ? $"动作模块通讯异常，等待 {Configuration.Motion.PortName} 重连并重发 {description}（最多 {MotionCommandMaxAttempts} 次）。"
                        : $"动作模块 {description} 连续 {MotionCommandMaxAttempts} 次未确认，本轮测试已停止。";
                AddLog($"动作模块 {description} 第 {attempt}/{MotionCommandMaxAttempts} 次未确认：{exception.Message}",
                    attempt < MotionCommandMaxAttempts ? "提示" : "错误");
                RaiseAllPropertiesChanged();
            }
        }
        throw new InvalidDataException($"动作模块 {description} 最多 {MotionCommandMaxAttempts} 次发送仍未收到匹配回复。", lastFailure);
    }

    private async Task EnterNoScanStandbyAsync()
    {
        if (!IsNoScanMode || IsSimulating || IsInstrumentTesting) return;
        try
        {
            SimulationSummary = Configuration.Motion.Enabled && IsMotionConnected
                ? $"不扫码模式已就绪，持续等待 X{Configuration.Motion.StartInput} 启动信号。"
                : "不扫码模式已就绪，持续等待操作员在气密仪面板按 Start。";
            RaiseAllPropertiesChanged();
            await OpenMotionAsync("不扫码模式自动待机");
        }
        catch (Exception exception)
        {
            SimulationSummary = "不扫码模式待机失败，请检查气密仪连接。";
            AddLog($"不扫码模式自动待机失败：{exception.Message}", "错误");
            RaiseAllPropertiesChanged();
        }
    }

    private Task ArmManualInstrumentTestAsync()
    {
        if (_instrumentClient is null || !IsInstrumentConnected) throw new InvalidOperationException("气密仪未连接。");
        if (IsInstrumentTesting) throw new InvalidOperationException("双通道气密测试正在进行。");

        var instrument = _instrumentClient;
        _manualStopRequested = false;
        var recipe = ConfigurationStore.Clone(Configuration);
        var cycleId = Guid.NewGuid();
        var cancellation = new CancellationTokenSource();
        _instrumentTestCancellation = cancellation;
        IsInstrumentTesting = true;
        PressureSamples.Clear();
        Channel1State = Channel2State = "等待面板 Start";
        SimulationSummary = $"上位机已就绪，请在气密仪面板按 Start（最长等待 {recipe.ManualStartWaitTimeoutSeconds} 秒）。";
        AddLog($"上位机已就绪，未向气密仪发送启动指令；请在仪器面板按 Start。检测到任一通道实时压力变化后将自动采集双通道波形。", "提示");
        RaiseAllPropertiesChanged();
        _instrumentTestTask = WaitForManualInstrumentStartAsync(instrument, recipe, cycleId, cancellation);
        return Task.CompletedTask;
    }

    private async Task WaitForManualInstrumentStartAsync(ByzModbusRtuClient instrument, AirtightnessConfiguration recipe, Guid cycleId, CancellationTokenSource cancellation)
    {
        var monitoringStarted = false;
        float? idlePressure1 = null;
        float? idlePressure2 = null;
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (timer.Elapsed.TotalSeconds < recipe.ManualStartWaitTimeoutSeconds)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var pressure1 = await instrument.ReadChannelPressureAsync(1, cancellation.Token);
                var pressure2 = await instrument.ReadChannelPressureAsync(2, cancellation.Token);
                UpdatePressureDiagnostics(pressure1, pressure2, "等待压力变化");
                idlePressure1 ??= pressure1;
                idlePressure2 ??= pressure2;
                var pressureChanged = Math.Abs(pressure1 - idlePressure1.Value) >= 0.5f || Math.Abs(pressure2 - idlePressure2.Value) >= 0.5f;
                if (pressureChanged)
                {
                    monitoringStarted = true;
                    AddLog($"已检测到实时压力变化：CH1 {pressure1:0.000} kPa；CH2 {pressure2:0.000} kPa。开始采集实时波形。", "信息");
                    await MonitorInstrumentTestAsync(instrument, recipe, DateTimeOffset.Now, cycleId, cancellation);
                    return;
                }

                var seconds = timer.Elapsed.TotalSeconds;
                Channel1State = Channel2State = "等待面板 Start";
                SimulationSummary = $"条码/I-O 已就绪，请在气密仪面板按 Start（{seconds:0} / {recipe.ManualStartWaitTimeoutSeconds} 秒）。";
                RaiseAllPropertiesChanged();
                await Task.Delay(250, cancellation.Token);
            }

            Channel1State = Channel2State = "等待启动超时";
            SimulationSummary = "未检测到仪器面板 Start，未保存测试记录和统计。";
            AddLog($"等待仪器面板 Start 超时（{recipe.ManualStartWaitTimeoutSeconds} 秒），未保存测试记录和统计。", "提示");
        }
        catch (OperationCanceledException)
        {
            Channel1State = Channel2State = "已停止";
            SimulationSummary = "等待仪器面板 Start 已取消，未保存测试记录和统计。";
            AddLog("等待仪器面板 Start 已取消。", "提示");
        }
        catch (Exception exception)
        {
            if (cancellation.IsCancellationRequested)
            {
                Channel1State = Channel2State = "已停止";
                SimulationSummary = "等待仪器面板 Start 已取消，未保存测试记录和统计。";
                AddLog("等待仪器面板 Start 已取消。", "提示");
                return;
            }
            Channel1State = Channel2State = "通讯异常";
            SimulationSummary = "等待仪器面板 Start 时通讯异常，未保存测试记录和统计。";
            SetDiagnosticError(exception.Message);
            AddLog($"等待仪器面板 Start 时通讯异常：{exception.Message}", "错误");
        }
        finally
        {
            if (!monitoringStarted)
            {
                await CloseMotionOutputsAsync();
                IsInstrumentTesting = false;
                FinishInstrumentTest(cancellation, resumeNoScanStandby: true);
            }
        }
    }

    private async Task MonitorInstrumentTestAsync(ByzModbusRtuClient instrument, AirtightnessConfiguration recipe, DateTimeOffset started, Guid cycleId, CancellationTokenSource cancellation)
    {
        var completed = false;
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var duration = (double)(recipe.FillSeconds + recipe.BalanceSeconds + recipe.TestSeconds + recipe.ExhaustSeconds);
            var deadline = duration + 5d;
            AddLog($"实时波形只读取已验证的压力地址；将在工艺时间 {duration:0.0} 秒加 5 秒安全余量后读取最终结果。", "提示");

            while (timer.Elapsed.TotalSeconds < deadline)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var pressure1 = await instrument.ReadChannelPressureAsync(1, cancellation.Token);
                var pressure2 = await instrument.ReadChannelPressureAsync(2, cancellation.Token);
                UpdatePressureDiagnostics(pressure1, pressure2, "实时采集");
                var seconds = timer.Elapsed.TotalSeconds;
                PressureSamples.Add(new PressureSample(seconds, pressure1, pressure2, 0, 0, StageFor(recipe, seconds)));
                Pressure1Text = pressure1.ToString("0.000");
                Pressure2Text = pressure2.ToString("0.000");
                Leak1Text = Leak2Text = "待最终结果";
                Channel1State = $"{StageFor(recipe, seconds)} · 压力采集";
                Channel2State = $"{StageFor(recipe, seconds)} · 压力采集";
                SimulationSummary = $"双通道实测 · {StageFor(recipe, seconds)} · {seconds:0.0} / {duration:0.0} s";
                RaiseAllPropertiesChanged();

                await Task.Delay(250, cancellation.Token);
            }

            var final1 = await instrument.ReadChannelFinalMeasurementAsync(1, cancellation.Token);
            var final2 = await instrument.ReadChannelFinalMeasurementAsync(2, cancellation.Token);
            CompleteInstrumentCycle(recipe, started, cycleId, final1, final2);
            completed = true;
        }
        catch (OperationCanceledException)
        {
            Channel1State = Channel2State = "已停止";
            SimulationSummary = "双通道实测已停止，未保存记录和统计。";
            AddLog("双通道气密实测已停止。", "提示");
        }
        catch (Exception exception)
        {
            if (cancellation.IsCancellationRequested)
            {
                Channel1State = Channel2State = "已停止";
                SimulationSummary = "双通道实测已停止，未保存记录和统计。";
                AddLog("双通道气密实测已停止。", "提示");
                return;
            }
            Channel1State = Channel2State = "通讯异常";
            SimulationSummary = "双通道实测通讯异常，未保存记录和统计。";
            SetDiagnosticError(exception.Message);
            AddLog($"双通道气密实测异常：{exception.Message}", "错误");
        }
        finally
        {
            if (completed) await CloseMotionOutputsAfterDelayAsync(recipe.Motion.OutputReleaseDelaySeconds);
            else await CloseMotionOutputsAsync();
            FinishInstrumentTest(cancellation, resumeNoScanStandby: completed);
        }
    }

    /// <summary>不扫码模式的显式待机入口；不会向气密仪发送启动指令。</summary>
    public async Task PrepareInstrumentTestAsync()
    {
        if (!IsNoScanMode) throw new InvalidOperationException("当前扫码模式需在双通道条码齐全后自动准备测试。");
        if (!IsInstrumentConnected || _instrumentClient is null) throw new InvalidOperationException("请先连接气密仪。");
        _continuousInstrumentMonitoringEnabled = true;
        AddLog("已启用持续待机：每轮测试完成后将自动等待下一次仪器面板 Start，直到操作员点击停止。", "信息");
        RaiseAllPropertiesChanged();
        await EnterNoScanStandbyAsync();
    }

    public async Task StopInstrumentTestAsync()
    {
        if (!IsInstrumentTesting && !_continuousInstrumentMonitoringEnabled) return;
        _continuousInstrumentMonitoringEnabled = false;
        if (!IsInstrumentTesting)
        {
            SimulationSummary = "已停止持续等待；气密仪保持连接。";
            AddLog("操作员已停止持续等待；气密仪保持连接。", "提示");
            RaiseAllPropertiesChanged();
            return;
        }

        _manualStopRequested = true;
        AddLog("操作员请求停止等待/实测，正在取消本轮气密监控。", "提示");
        _instrumentTestCancellation?.Cancel();
        // SerialPort read can ignore cancellation; closing this client's port releases the pending read.
        _instrumentClient?.AbortPendingExchange();
        _isInstrumentRecovering = true;
        InstrumentState = "连接中";
        RaiseAllPropertiesChanged();

        var monitoringTask = _instrumentTestTask;
        if (monitoringTask is null) return;
        var completed = await Task.WhenAny(monitoringTask, Task.Delay(TimeSpan.FromSeconds(5)));
        if (completed != monitoringTask)
        {
            SimulationSummary = "停止请求已发送，正在等待串口读操作释放；请稍候，勿重复连接。";
            AddLog("停止等待超过 5 秒：串口读操作尚在释放，保持停止中状态。", "提示");
            RaiseAllPropertiesChanged();
            return;
        }
        await monitoringTask;
    }
    private void FinishInstrumentTest(CancellationTokenSource cancellation, bool resumeNoScanStandby)
    {
        if (!ReferenceEquals(_instrumentTestCancellation, cancellation)) return;
        var restoreAfterManualStop = _manualStopRequested && !_isDisconnectingInstrument && _instrumentClient is not null;
        IsInstrumentTesting = false;
        _instrumentTestCancellation = null;
        _instrumentTestTask = null;
        cancellation.Dispose();
        var resumeContinuousStandby = resumeNoScanStandby && _continuousInstrumentMonitoringEnabled && !restoreAfterManualStop
            && IsNoScanMode && IsInstrumentConnected;
        _manualStopRequested = false;
        RaiseAllPropertiesChanged();
        if (restoreAfterManualStop) _ = RestoreInstrumentConnectionAfterStopAsync();
        else if (resumeContinuousStandby) _ = ResumeContinuousStandbyAsync();
    }

    private async Task ResumeContinuousStandbyAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        if (!_continuousInstrumentMonitoringEnabled || IsInstrumentTesting || !IsNoScanMode || !IsInstrumentConnected) return;
        AddLog("本轮气密测试结束，继续等待下一次仪器面板 Start。", "信息");
        await EnterNoScanStandbyAsync();
    }

    private async Task RestoreInstrumentConnectionAfterStopAsync()
    {
        if (_instrumentClient is null || _isDisconnectingInstrument) return;
        try
        {
            await _instrumentClient.ReconnectAsync();
            InstrumentState = "已连接";
            SimulationSummary = "等待/测试已停止，气密仪已恢复连接；现在可修改并同步气密配置。";
            AddLog("气密仪已恢复连接；现在可修改并同步气密配置。", "信息");
        }
        catch (Exception exception)
        {
            var failedInstrument = _instrumentClient;
            _instrumentClient = null;
            if (failedInstrument is not null) await failedInstrument.DisposeAsync();
            InstrumentState = "未连接";
            SimulationSummary = "停止后气密仪重连失败，请检查串口后重新连接。";
            AddLog($"停止后气密仪重连失败：{exception.Message}", "错误");
        }
        finally
        {
            _isInstrumentRecovering = false;
            RaiseAllPropertiesChanged();
        }
    }

    private void UpdateDiagnostics(ByzChannelStageStatus? status1, ByzChannelStageStatus? status2,
        ByzChannelMeasurement channel1, ByzChannelMeasurement channel2)
    {
        DiagnosticChannel1 = $"阶段 6021–6025：{status1?.Description ?? "未读取"}；气压@30：{channel1.PressureKpa:0.000} kPa；压降@350：{channel1.LeakPa:0.00} Pa；结果码@11：{channel1.ResultCode}";
        DiagnosticChannel2 = $"阶段 6051–6055：{status2?.Description ?? "未读取"}；气压@30：{channel2.PressureKpa:0.000} kPa；压降@350：{channel2.LeakPa:0.00} Pa；结果码@11：{channel2.ResultCode}";
        DiagnosticUpdatedText = $"最后刷新：{DateTime.Now:HH:mm:ss.fff}";
        DiagnosticErrorText = "无";
    }

    private void UpdatePressureDiagnostics(float pressure1, float pressure2, string state)
    {
        DiagnosticChannel1 = $"启动判定：仅压力变化；状态：{state}；气压@30：{pressure1:0.000} kPa；压降/结果码：待最终结果读取";
        DiagnosticChannel2 = $"启动判定：仅压力变化；状态：{state}；气压@1030：{pressure2:0.000} kPa；压降/结果码：待最终结果读取";
        DiagnosticUpdatedText = $"最后刷新：{DateTime.Now:HH:mm:ss.fff}";
        DiagnosticErrorText = "无";
        if ((DateTime.UtcNow - _lastDiagnosticLogAt).TotalSeconds >= 1)
        {
            _lastDiagnosticLogAt = DateTime.UtcNow;
            AddLog($"实时通讯诊断：{state}；CH1 压力@30={pressure1:0.000} kPa；CH2 压力@1030={pressure2:0.000} kPa。", "诊断");
        }
    }

    private void SetDiagnosticError(string message)
    {
        DiagnosticErrorText = message;
        DiagnosticUpdatedText = $"读取失败：{DateTime.Now:HH:mm:ss.fff}";
    }

    private void CompleteInstrumentCycle(AirtightnessConfiguration recipe, DateTimeOffset started, Guid cycleId,
        ByzChannelMeasurement final1, ByzChannelMeasurement final2)
    {
        // Confirmed by vendor: result code 0=PASS, 1=FAIL. Device result is the production decision.
        var pass1 = final1.ResultCode == 0;
        var pass2 = final2.ResultCode == 0;
        var result1 = new AirtightnessChannelResult(1, (decimal)final1.PressureKpa, (decimal)final1.LeakPa, pass1, final1.ResultCode);
        var result2 = new AirtightnessChannelResult(2, (decimal)final2.PressureKpa, (decimal)final2.LeakPa, pass2, final2.ResultCode);
        _results.RecordCompletedCycle(cycleId, started, Session.UserName, recipe, Channel1Barcode, Channel2Barcode, result1, result2);
        AddLog($"气密 CSV 已保存：{_results.GetCsvFilePath(started)}。", "信息");
        Pressure1Text = final1.PressureKpa.ToString("0.000");
        Pressure2Text = final2.PressureKpa.ToString("0.000");
        Leak1Text = final1.LeakPa.ToString("0.00");
        Leak2Text = final2.LeakPa.ToString("0.00");
        InstrumentResult1Text = final1.ResultCode.ToString();
        InstrumentResult2Text = final2.ResultCode.ToString();
        Channel1State = pass1 ? "PASS" : "FAIL";
        Channel2State = pass2 ? "PASS" : "FAIL";
        SimulationSummary = $"双通道实测完成 · CH1 {(pass1 ? "PASS" : "FAIL")} · CH2 {(pass2 ? "PASS" : "FAIL")}";
        AddLog($"双通道实测完成：CH1 {(pass1 ? "PASS" : "FAIL")}（仪器结果码 {final1.ResultCode}），CH2 {(pass2 ? "PASS" : "FAIL")}（仪器结果码 {final2.ResultCode}）。", "信息");
        ClearBarcodes();
        TestCycleCompleted?.Invoke();
        Refresh();
        if (IsAutomaticScanMode && IsScannerConnected) _ = BeginAutomaticScanAsync();
    }

    private async Task CloseMotionOutputsAsync()
    {
        var motion = _motionClient;
        if (motion is null) return;
        var allConfirmed = true;
        foreach (var output in new[] { Configuration.Motion.Channel1Output, Configuration.Motion.Channel2Output })
        {
            try
            {
                await ExecuteMotionWithRecoveryAsync(motion,
                    async () => { await motion.SetOutputAsync(output, false); return true; }, $"关闭 Y{output}");
            }
            catch (Exception exception)
            {
                allConfirmed = false;
                AddLog($"Y{output} 关闭未确认，请现场核查该输出状态：{exception.Message}", "错误");
            }
        }
        if (allConfirmed)
            AddLog("双通道实测结束，已关闭 CH1、CH2 动作输出。", "信息");
    }

    private async Task CloseMotionOutputsAfterDelayAsync(decimal delaySeconds)
    {
        if (delaySeconds > 0)
        {
            AddLog($"双通道测试结果已收到，Y 输出将保持 {delaySeconds:0.###} 秒后断开。", "信息");
            await Task.Delay(TimeSpan.FromSeconds((double)delaySeconds));
        }
        await CloseMotionOutputsAsync();
    }

    private static string StageFor(AirtightnessConfiguration recipe, double seconds)
    {
        if (seconds < (double)recipe.FillSeconds) return "充气";
        if (seconds < (double)(recipe.FillSeconds + recipe.BalanceSeconds)) return "平衡";
        if (seconds < (double)(recipe.FillSeconds + recipe.BalanceSeconds + recipe.TestSeconds)) return "检测";
        return "排气";
    }

    private void Scanner_DataReceived(object? sender, DeviceDataReceivedEventArgs e)
    {
        var text = e.Text.Trim();
        if (string.IsNullOrWhiteSpace(text) || !IsAutomaticScanMode || !_automaticScanRunning) return;
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            var channel = string.IsNullOrWhiteSpace(Channel1Barcode) ? 1 : 2;
            try
            {
                var result = AcceptBarcode(channel, text, automatic: true);
                if (result == 1 && _automaticScanRunning) await TriggerScannerAsync(2);
            }
            catch (Exception exception) { AddLog($"自动扫码数据无效：{exception.Message}", "错误"); }
        });
    }

    private async Task TriggerScannerAsync(int channel)
    {
        if (_scannerConnection is null || !_scannerConnection.IsOpen)
            throw new InvalidOperationException("扫码枪未连接，无法发送自动扫码指令。");

        if (Configuration.Scanner.ConnectionType == ConnectionType.UsbPosHid)
        {
            AddLog($"CH{channel}：USB POS HID 扫码枪不支持软件触发，请按设备实体按键扫码。", "提示");
            return;
        }
        if (!Configuration.ScannerTriggerEnabled)
        {
            AddLog($"CH{channel}：未启用自动扫码触发，等待扫码枪主动上报条码。", "提示");
            return;
        }

        var command = DecodeCommand(Configuration.ScannerTriggerCommand);
        if (string.IsNullOrEmpty(command))
        {
            AddLog($"CH{channel}：扫码触发命令为空，等待扫码枪主动上报条码。", "提示");
            return;
        }
        await _scannerConnection.SendStringAsync(command);
        AddLog($"CH{channel}：已发送扫码触发指令 {FormatCommandForLog(command)}。", "信息");
    }

    private async Task StopScannerAsync()
    {
        if (Configuration.Scanner.ConnectionType == ConnectionType.UsbPosHid || _scannerConnection is null || !_scannerConnection.IsOpen) return;
        var command = DecodeCommand(Configuration.ScannerStopCommand);
        if (string.IsNullOrEmpty(command)) return;
        try
        {
            await _scannerConnection.SendStringAsync(command);
            AddLog($"已发送扫码停止指令 {FormatCommandForLog(command)}。", "信息");
        }
        catch (Exception exception) { AddLog($"扫码停止指令发送失败：{exception.Message}", "错误"); }
    }

    private void StartScannerWait()
    {
        CancelScannerWait();
        var cancellation = new CancellationTokenSource();
        _scannerWaitCancellation = cancellation;
        _ = HandleScannerWaitAsync(cancellation);
    }

    private async Task HandleScannerWaitAsync(CancellationTokenSource cancellation)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, Configuration.ScannerWaitTimeoutSeconds)), cancellation.Token); }
        catch (OperationCanceledException) { return; }
        if (!ReferenceEquals(_scannerWaitCancellation, cancellation)) return;

        _scannerWaitCancellation = null;
        cancellation.Dispose();
        _automaticScanRunning = false;
        await StopScannerAsync();
        SimulationSummary = $"自动扫码超时（{Configuration.ScannerWaitTimeoutSeconds} 秒），未收到完整的 CH1、CH2 条码。";
        AddLog(SimulationSummary, "错误");
        RaiseAllPropertiesChanged();
    }

    private void CancelScannerWait()
    {
        _scannerWaitCancellation?.Cancel();
        _scannerWaitCancellation?.Dispose();
        _scannerWaitCancellation = null;
    }

    private static string DecodeCommand(string value) => (value ?? string.Empty)
        .Replace("\\r", "\r", StringComparison.Ordinal)
        .Replace("\\n", "\n", StringComparison.Ordinal)
        .Replace("\\t", "\t", StringComparison.Ordinal);

    private static string FormatCommandForLog(string command) => command
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);

    public void AddLog(string message, string level = "信息")
    {
        var timestamp = DateTime.Now;
        // Keep each event on one line; file logging is isolated from production errors.
        message = message.Replace('\r', ' ').Replace('\n', ' ');
        Logs.Insert(0, $"{timestamp:yyyy-MM-dd HH:mm:ss.fff}  [{level}] {message}");
        while (Logs.Count > 1000) Logs.RemoveAt(Logs.Count - 1);
        _logWriter.Write(LogDirectory, timestamp, $"[{level}] {message}");
    }

    public void StopSimulation() => _simulationCancellation?.Cancel();

    public async Task DisconnectDevicesAsync()
    {
        CancelScannerWait();
        _automaticScanRunning = false;
        _isDisconnectingInstrument = true;
        try
        {
            _instrumentTestCancellation?.Cancel();
            if (_instrumentTestTask is not null) await _instrumentTestTask;
        }
        finally { _isDisconnectingInstrument = false; }
        await CloseMotionOutputsAsync();
        if (_scannerConnection is not null)
        {
            _scannerConnection.DataReceived -= Scanner_DataReceived;
            await _scannerConnection.DisposeAsync();
            _scannerConnection = null;
        }
        if (_instrumentClient is not null)
        {
            await _instrumentClient.DisposeAsync();
            _instrumentClient = null;
        }
        if (_motionClient is not null)
        {
            await _motionClient.DisposeAsync();
            _motionClient = null;
        }
        ScannerState = InstrumentState = MotionState = "未连接";
        RaiseAllPropertiesChanged();
    }

    public async Task RunSimulationAsync()
    {
        if (IsSimulating) { StopSimulation(); return; }
        var recipe = ConfigurationStore.Clone(Configuration);
        using var cancellation = new CancellationTokenSource();
        _simulationCancellation = cancellation;
        IsSimulating = true;
        PressureSamples.Clear();
        var failChannel2 = ++_simulationNumber % 2 == 0;
        var started = DateTime.Now;
        AddLog("[模拟] 开始双通道测试，仅预览，不写入正式测试记录和统计。");
        RaiseAllPropertiesChanged();
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var duration = AirtightnessSimulation.Duration(recipe);
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var sample = AirtightnessSimulation.Sample(recipe, timer.Elapsed.TotalSeconds, failChannel2);
                PressureSamples.Add(sample);
                if (PressureSamples.Count > 2400)
                    for (var i = PressureSamples.Count - 2; i > 0; i -= 2) PressureSamples.RemoveAt(i);
                Pressure1Text = sample.Channel1.ToString("0.000");
                Pressure2Text = sample.Channel2.ToString("0.000");
                Leak1Text = sample.Leak1.ToString("0.00");
                Leak2Text = sample.Leak2.ToString("0.00");
                Channel1State = Channel2State = $"模拟 · {sample.Stage}";
                SimulationSummary = $"模拟测试 · {sample.Stage} · {sample.Seconds:0.0} / {duration:0.0} s（不计入生产统计）";
                RaiseAllPropertiesChanged();
                if (sample.Seconds >= duration) break;
                await Task.Delay(50, cancellation.Token);
            }
            var last = PressureSamples.Last();
            var pressure = ((double)recipe.MinimumPressureKpa + (double)recipe.MaximumPressureKpa) / 2;
            Channel1State = "模拟 PASS";
            Channel2State = failChannel2 ? "模拟 FAIL" : "模拟 PASS";
            var result = recipe.CountingMode == AirtightnessCountingMode.JointProduct
                ? failChannel2 ? "FAIL（1件）" : "PASS（1件）"
                : failChannel2 ? "PASS 1 / FAIL 1（2件）" : "PASS 2 / FAIL 0（2件）";
            SimulationSummary = $"模拟完成 · {result} · 不计入生产统计，波形已保留";
            SimulationHistory.Insert(0, new(started.ToString("yyyy-MM-dd HH:mm:ss"), Session.UserName,
                "[模拟] " + recipe.ProductName, pressure.ToString("0.000"), last.Leak1.ToString("0.00"),
                pressure.ToString("0.000"), last.Leak2.ToString("0.00"), "模拟 " + result));
            while (SimulationHistory.Count > 100) SimulationHistory.RemoveAt(SimulationHistory.Count - 1);
            AddLog($"[模拟] {result}；CH1 {last.Leak1:0.00} Pa，CH2 {last.Leak2:0.00} Pa。");
            ClearBarcodes();
            TestCycleCompleted?.Invoke();
            AddLog("测试完成，已清除 CH1、CH2 条码，等待 CH1 下一次扫码。", "信息");
        }
        catch (OperationCanceledException)
        {
            Channel1State = Channel2State = "模拟已停止";
            SimulationSummary = "模拟已停止，未生成完成记录，未计入生产统计。";
            AddLog("[模拟] 用户停止模拟测试。");
        }
        finally
        {
            IsSimulating = false;
            _simulationCancellation = null;
            RaiseAllPropertiesChanged();
        }
    }

    public void Refresh()
    {
        Refresh(_results.LoadAll());
    }

    private void Refresh(IReadOnlyList<AirtightnessCycle> records)
    {
        var currentRecords = Configuration.StatisticsStartAt is { } start
            ? records.Where(record => record.StartedAt >= start).ToList()
            : records;
        Statistics = AirtightnessStatistics.Calculate(currentRecords, Configuration.CountingMode);
        History.Clear();
        foreach (var cycle in currentRecords.Where(r => r.Recipe.CountingMode == Configuration.CountingMode).Take(100))
        {
            var ch1 = cycle.Channels.SingleOrDefault(c => c.Channel == 1);
            var ch2 = cycle.Channels.SingleOrDefault(c => c.Channel == 2);
            var result = cycle.Recipe.CountingMode == AirtightnessCountingMode.JointProduct
                ? cycle.Channels.Count < 2 ? "等待另一通道" : cycle.Channels.All(cycle.IsPass) ? "PASS" : "FAIL"
                : string.Join(" / ", cycle.Channels.OrderBy(c => c.Channel).Select(c => $"CH{c.Channel} {(cycle.IsPass(c) ? "PASS" : "FAIL")}"));
            History.Add(new(cycle.StartedAt.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"), cycle.Operator,
                cycle.Recipe.ProductName, ch1?.PressureKpa.ToString("0.##") ?? "—", ch1?.LeakPa.ToString("0.##") ?? "—",
                ch2?.PressureKpa.ToString("0.##") ?? "—", ch2?.LeakPa.ToString("0.##") ?? "—", result));
        }
        RaiseAllPropertiesChanged();
        AddLog($"测试记录已刷新：{CountingModeText}；总数 {Statistics.Total}，PASS {Statistics.Pass}，FAIL {Statistics.Fail}。");
    }
}

public sealed record AirtightnessHistoryRow(string Time, string Operator, string Product, string Pressure1,
    string Leak1, string Pressure2, string Leak2, string Result);
