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
    private bool _automaticScanRunning;
    private bool _scanStartRequested;
    public ObservableCollection<string> Logs { get; } = new();
    public string LogDirectory => _store.GetLogDirectory(Configuration);
    public string RecordsDirectory => _store.GetRecordsDirectory(Configuration);
    public UserSession Session { get; }
    public AirtightnessConfiguration Configuration { get; private set; }
    public AirtightnessStatistics Statistics { get; private set; } = new(0, 0, 0, 0, 0, 0);
    public string WindowTitle => $"CeHao · 气密检测 · {Configuration.ProductName}";
    public string LoginDisplay => $"{Session.DisplayName} / {Session.RoleText}";
    public bool CanConfigure => Session.CanManageSystem && !IsSimulating && !IsInstrumentTesting;
    private CancellationTokenSource? _simulationCancellation;
    private int _simulationNumber;
    public bool IsSimulating { get; private set; }
    public bool IsInstrumentTesting { get; private set; }
    public bool CanSwitchAccount => !IsSimulating && !IsInstrumentTesting;
    public bool CanStartScan => !IsSimulating && !IsInstrumentTesting;
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
    public string Pressure1Text { get; private set; } = "—";
    public string Pressure2Text { get; private set; } = "—";
    public string Leak1Text { get; private set; } = "—";
    public string Leak2Text { get; private set; } = "—";
    public double WaveformDuration => AirtightnessSimulation.Duration(Configuration);
    public double WaveformMaximum => (double)Configuration.MaximumPressureKpa * 1.15;
    public ObservableCollection<PressureSample> PressureSamples { get; } = new();
    public ObservableCollection<AirtightnessHistoryRow> SimulationHistory { get; } = new();
    public string CountingModeText => Configuration.CountingMode == AirtightnessCountingMode.SeparateProducts
        ? "双产品模式 · 每个通道独立计 1 件" : "单产品模式 · 两通道共同判定 1 件";
    public string CountingNote => "统计当前保存目录内的记录，两种模式分别累计；旧目录文件保留。";
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
        SimulationSummary = "配置已更新并已由气密仪回读确认；可使用模拟测试预览双通道压力变化。";
        AddLog($"气密配置已保存：产品 {Configuration.ProductName}；{CountingModeText}；气压 {PressureRange}；充气 {FillTime}，平衡 {BalanceTime}，测试 {TestTime}，泄气 {ExhaustTime}；压降 {LeakLimit}。");
        AddLog($"保存路径：测试记录 {RecordsDirectory}；日志 {LogDirectory}。");
        Refresh(records);
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
        AddLog($"设备连接配置已保存：扫码枪 {Configuration.Scanner.ConnectionType}，自动触发{(Configuration.ScannerTriggerEnabled ? "已启用" : "未启用")}，等待 {Configuration.ScannerWaitTimeoutSeconds} 秒；气密仪 {instrument.PortName} / {instrument.BaudRate} / 站号 {instrument.StationAddress}；动作模块 {motion.PortName} / {motion.BaudRate} / 站号 {motion.StationAddress}，CH1=Y{motion.Channel1Output}，CH2=Y{motion.Channel2Output}，输出控制{(motion.Enabled ? "已允许" : "未启用")}。", "信息");
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
        AddLog($"双通道动作设置已保存：{motion.PortName} / {motion.BaudRate} / 站号 {motion.StationAddress}；CH1=Y{motion.Channel1Output}，CH2=Y{motion.Channel2Output}；输出控制{(motion.Enabled ? "已允许" : "未启用")}。", "信息");
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
        }
        catch
        {
            scanner.DataReceived -= Scanner_DataReceived;
            await scanner.DisposeAsync();
            throw;
        }
        finally { RaiseAllPropertiesChanged(); }
    }

    /// <summary>
    /// 主界面“开始测试”的扫码阶段。自动模式依次触发 CH1、CH2；此方法不下发气密测试命令。
    /// </summary>
    public async Task StartScanAsync()
    {
        if (IsSimulating) throw new InvalidOperationException("模拟测试正在进行，请先停止模拟测试。");

        if (IsNoScanMode)
        {
            _scanStartRequested = true;
            SimulationSummary = "当前为不扫码模式，正在请求开启双通道 I/O 输出。";
            AddLog("开始测试：当前为不扫码模式，跳过扫码枪并请求开启双通道 I/O 输出。", "提示");
            RaiseAllPropertiesChanged();
            await OpenMotionAsync("不扫码模式");
            return;
        }

        if (IsManualScanMode)
        {
            _automaticScanRunning = false;
            _scanStartRequested = true;
            if (IsBarcodePairReady)
            {
                SimulationSummary = "CH1、CH2 条码均已存在，正在按条码条件请求开启双通道 I/O 输出。";
                AddLog("开始测试：手动扫码模式检测到 CH1、CH2 条码已齐全，请求开启双通道 I/O 输出。", "提示");
                if (Configuration.Motion.Enabled && Configuration.Motion.AutoOpenAfterBarcode)
                    await OpenMotionAsync("手动扫码条码齐全");
                else
                    AddLog("条码已齐全，但未启用“条码齐全后自动开启动作输出”，未发送 I/O 开启命令。", "提示");
            }
            else
            {
                SimulationSummary = "请扫描或输入 CH1、CH2 条码；两码齐全后将请求开启双通道 I/O 输出。";
                AddLog("开始测试：手动扫码模式，等待 CH1、CH2 条码。", "提示");
            }
            RaiseAllPropertiesChanged();
            return;
        }

        if (!IsScannerConnected || _scannerConnection is null)
            throw new InvalidOperationException("自动扫码前请先在“设备连接”中连接扫码枪。");

        ClearBarcodes();
        _scanStartRequested = true;
        _automaticScanRunning = true;
        StartScannerWait();
        SimulationSummary = "已开始自动扫码，正在等待 CH1 条码。";
        AddLog("开始测试：已触发自动扫码，等待 CH1 条码。", "提示");
        RaiseAllPropertiesChanged();
        await TriggerScannerAsync(1);
    }

    public async Task ToggleInstrumentConnectionAsync()
    {
        if (IsInstrumentConnected)
        {
            await _instrumentClient!.DisconnectAsync();
            await _instrumentClient.DisposeAsync();
            _instrumentClient = null;
            InstrumentState = "未连接";
            AddLog("气密仪已断开。");
            RaiseAllPropertiesChanged();
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
        if (IsMotionConnected)
        {
            await _motionClient!.DisconnectAsync();
            await _motionClient.DisposeAsync();
            _motionClient = null;
            MotionState = "未连接";
            AddLog("双通道动作控制模块已断开。");
            RaiseAllPropertiesChanged();
            return;
        }
        var motion = new DigitalIoModbusRtuClient(Configuration.Motion);
        try
        {
            await motion.ConnectAsync();
            _ = await motion.ReadOutputAsync(Configuration.Motion.Channel1Output);
            _motionClient = motion;
            MotionState = "已连接";
            AddLog($"双通道动作控制模块已连接：{Configuration.Motion.PortName} / {Configuration.Motion.BaudRate} / 站号 {Configuration.Motion.StationAddress}。", "信息");
        }
        catch
        {
            await motion.DisposeAsync();
            throw;
        }
        finally { RaiseAllPropertiesChanged(); }
    }

    public async Task SetMotionOutputAsync(int channel, bool enabled)
    {
        if (!CanConfigure) throw new UnauthorizedAccessException("当前账号无权控制气密动作继电器。");
        if (!Configuration.Motion.Enabled) throw new InvalidOperationException("动作控制未启用。请先在双通道动作设置中确认接线并勾选“允许上位机控制继电器”。");
        if (!IsMotionConnected || _motionClient is null) throw new InvalidOperationException("动作控制模块未连接。");
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
        SimulationSummary = "双通道条码已就绪；正在按测试流程开启 I/O 并启动 BYD 双通道气密仪。";
        _ = StopScannerAsync();
        if (_scanStartRequested && Configuration.Motion.Enabled && Configuration.Motion.AutoOpenAfterBarcode && !_motionTriggeredForBarcodePair)
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
        if (!Configuration.Motion.Enabled)
            throw new InvalidOperationException("动作控制未启用。请先在“双通道动作设置”中勾选“允许上位机控制继电器”。");
        if (!IsMotionConnected || _motionClient is null)
            throw new InvalidOperationException("I/O 动作控制模块未连接，未发送开启输出命令。");
        if (!IsInstrumentConnected || _instrumentClient is null)
            throw new InvalidOperationException("气密仪未连接，未发送 I/O 开启或气密测试启动指令。");

        await _motionClient.SetOutputAsync(Configuration.Motion.Channel1Output, true);
        await _motionClient.SetOutputAsync(Configuration.Motion.Channel2Output, true);
        try
        {
            AddLog($"{source}：已开启 CH1/Y{Configuration.Motion.Channel1Output} 与 CH2/Y{Configuration.Motion.Channel2Output} 动作输出。", "信息");
            await StartInstrumentTestAsync();
            _scanStartRequested = false;
        }
        catch
        {
            await CloseMotionOutputsAsync();
            throw;
        }
    }

    private async Task StartInstrumentTestAsync()
    {
        if (_instrumentClient is null || !IsInstrumentConnected) throw new InvalidOperationException("气密仪未连接。");
        if (IsInstrumentTesting) throw new InvalidOperationException("双通道气密测试正在进行。");

        var recipe = ConfigurationStore.Clone(Configuration);
        var started = DateTimeOffset.Now;
        var cycleId = Guid.NewGuid();
        var cancellation = new CancellationTokenSource();
        _instrumentTestCancellation = cancellation;
        IsInstrumentTesting = true;
        PressureSamples.Clear();
        Channel1State = Channel2State = "启动中";
        SimulationSummary = "已发送双通道气密仪启动指令，正在采集实时压力。";
        RaiseAllPropertiesChanged();
        try
        {
            await _instrumentClient.StartChannelAsync(1, cancellation.Token);
            await _instrumentClient.StartChannelAsync(2, cancellation.Token);
            AddLog("已按 BYD 双通道协议发送 CH1、CH2 启动线圈指令。", "信息");
            _ = MonitorInstrumentTestAsync(recipe, started, cycleId, cancellation);
        }
        catch
        {
            IsInstrumentTesting = false;
            _instrumentTestCancellation = null;
            cancellation.Dispose();
            RaiseAllPropertiesChanged();
            throw;
        }
    }

    private async Task MonitorInstrumentTestAsync(AirtightnessConfiguration recipe, DateTimeOffset started, Guid cycleId, CancellationTokenSource cancellation)
    {
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            var duration = (double)(recipe.FillSeconds + recipe.BalanceSeconds + recipe.TestSeconds + recipe.ExhaustSeconds);
            var deadline = duration + 5d;
            while (timer.Elapsed.TotalSeconds < deadline)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var ch1 = await _instrumentClient!.ReadChannelMeasurementAsync(1, cancellation.Token);
                var ch2 = await _instrumentClient.ReadChannelMeasurementAsync(2, cancellation.Token);
                var seconds = timer.Elapsed.TotalSeconds;
                PressureSamples.Add(new PressureSample(seconds, ch1.PressureKpa, ch2.PressureKpa, ch1.LeakPa, ch2.LeakPa, StageFor(recipe, seconds)));
                Pressure1Text = ch1.PressureKpa.ToString("0.000");
                Pressure2Text = ch2.PressureKpa.ToString("0.000");
                Leak1Text = ch1.LeakPa.ToString("0.00");
                Leak2Text = ch2.LeakPa.ToString("0.00");
                Channel1State = $"{StageFor(recipe, seconds)} · 结果码 {ch1.ResultCode}";
                Channel2State = $"{StageFor(recipe, seconds)} · 结果码 {ch2.ResultCode}";
                SimulationSummary = $"双通道实测 · {StageFor(recipe, seconds)} · {seconds:0.0} / {duration:0.0} s";
                RaiseAllPropertiesChanged();
                await Task.Delay(250, cancellation.Token);
            }

            var final1 = await _instrumentClient!.ReadChannelFinalMeasurementAsync(1, cancellation.Token);
            var final2 = await _instrumentClient.ReadChannelFinalMeasurementAsync(2, cancellation.Token);
            CompleteInstrumentCycle(recipe, started, cycleId, final1, final2);
        }
        catch (OperationCanceledException)
        {
            Channel1State = Channel2State = "已停止";
            SimulationSummary = "双通道实测已停止，未保存记录和统计。";
            AddLog("双通道气密实测已停止。", "提示");
        }
        catch (Exception exception)
        {
            Channel1State = Channel2State = "通讯异常";
            SimulationSummary = "双通道实测通讯异常，未保存记录和统计。";
            AddLog($"双通道气密实测异常：{exception.Message}", "错误");
        }
        finally
        {
            await CloseMotionOutputsAsync();
            IsInstrumentTesting = false;
            _instrumentTestCancellation?.Dispose();
            _instrumentTestCancellation = null;
            RaiseAllPropertiesChanged();
        }
    }

    private void CompleteInstrumentCycle(AirtightnessConfiguration recipe, DateTimeOffset started, Guid cycleId,
        ByzChannelMeasurement final1, ByzChannelMeasurement final2)
    {
        // The protocol exposes a numeric result code but does not define PASS/FAIL coding. Keep it in the log;
        // production acceptance is calculated from the configured pressure and leakage limits.
        var result1 = new AirtightnessChannelResult(1, (decimal)final1.PressureKpa, (decimal)final1.LeakPa, true);
        var result2 = new AirtightnessChannelResult(2, (decimal)final2.PressureKpa, (decimal)final2.LeakPa, true);
        _results.Record(cycleId, started, Session.UserName, recipe, result1);
        _results.Record(cycleId, started, Session.UserName, recipe, result2);
        var pass1 = final1.PressureKpa >= (float)recipe.MinimumPressureKpa && final1.PressureKpa <= (float)recipe.MaximumPressureKpa && final1.LeakPa <= (float)recipe.MaximumLeakPa;
        var pass2 = final2.PressureKpa >= (float)recipe.MinimumPressureKpa && final2.PressureKpa <= (float)recipe.MaximumPressureKpa && final2.LeakPa <= (float)recipe.MaximumLeakPa;
        Pressure1Text = final1.PressureKpa.ToString("0.000");
        Pressure2Text = final2.PressureKpa.ToString("0.000");
        Leak1Text = final1.LeakPa.ToString("0.00");
        Leak2Text = final2.LeakPa.ToString("0.00");
        Channel1State = pass1 ? "PASS" : "FAIL";
        Channel2State = pass2 ? "PASS" : "FAIL";
        SimulationSummary = $"双通道实测完成 · CH1 {(pass1 ? "PASS" : "FAIL")} · CH2 {(pass2 ? "PASS" : "FAIL")}";
        AddLog($"双通道实测完成：CH1 {(pass1 ? "PASS" : "FAIL")}（结果码 {final1.ResultCode}），CH2 {(pass2 ? "PASS" : "FAIL")}（结果码 {final2.ResultCode}）。", "信息");
        ClearBarcodes();
        Refresh();
    }

    private async Task CloseMotionOutputsAsync()
    {
        if (!IsMotionConnected || _motionClient is null) return;
        try
        {
            await _motionClient.SetOutputAsync(Configuration.Motion.Channel1Output, false);
            await _motionClient.SetOutputAsync(Configuration.Motion.Channel2Output, false);
            AddLog("双通道实测结束，已关闭 CH1、CH2 动作输出。", "信息");
        }
        catch (Exception exception) { AddLog($"关闭动作输出失败：{exception.Message}", "错误"); }
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
        _instrumentTestCancellation?.Cancel();
        if (_instrumentClient is not null && _instrumentClient.IsConnected)
        {
            try
            {
                await _instrumentClient.StopChannelAsync(1);
                await _instrumentClient.StopChannelAsync(2);
                AddLog("工作站关闭或断开前，已向 CH1、CH2 发送气密仪停止指令。", "提示");
            }
            catch (Exception exception) { AddLog($"发送气密仪停止指令失败：{exception.Message}", "错误"); }
        }
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
        Statistics = AirtightnessStatistics.Calculate(records, Configuration.CountingMode);
        History.Clear();
        foreach (var cycle in records.Where(r => r.Recipe.CountingMode == Configuration.CountingMode).Take(100))
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
