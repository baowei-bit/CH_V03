using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.Core.Communication;
using CH_V03.Core.Configuration;

namespace CH_V03.App.ViewModels;

public sealed class CommunicationConfigurationViewModel : DialogViewModelBase
{
    private readonly CommunicationTestService _communicationTestService;
    private string _scannerTestMessage = "未测试";
    private string _lowerMachineTestMessage = "未测试";
    private string _cameraResolution;

    public CommunicationConfigurationViewModel(AppConfiguration configuration, CommunicationTestService communicationTestService)
    {
        Configuration = configuration;
        _cameraResolution = $"{configuration.Communication.Scanner.CameraWidth} × {configuration.Communication.Scanner.CameraHeight}";
        _communicationTestService = communicationTestService;
        TestScannerCommand = new AsyncRelayCommand(TestScannerAsync);
        TestLowerMachineCommand = new AsyncRelayCommand(TestLowerMachineAsync);
        SaveCommand = new RelayCommand(() => { if (ApplyCameraSettings()) Close(true); });
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public AppConfiguration Configuration { get; }

    public ConnectionType[] ScannerConnectionTypes { get; } = Enum.GetValues<ConnectionType>();

    public ConnectionType[] LowerMachineConnectionTypes { get; } = Enum.GetValues<ConnectionType>()
        .Where(type => type != ConnectionType.UsbPosHid && type != ConnectionType.CameraQr)
        .ToArray();

    public string[] SerialPorts { get; } = GetSerialPortNames();

    public int[] CameraIndexes { get; } = Enumerable.Range(0, 21).ToArray();
    public string[] CameraResolutions { get; } = { "640 × 480", "1280 × 720", "1600 × 1200", "1920 × 1080", "2048 × 1536", "2592 × 1944", "3840 × 2160" };
    public string CameraResolution
    {
        get => _cameraResolution;
        set => SetField(ref _cameraResolution, value);
    }

    private bool ApplyCameraSettings()
    {
        var scanner = Configuration.Communication.Scanner;
        if (scanner.ConnectionType != ConnectionType.CameraQr) return true;
        var parts = CameraResolution.Replace('x', '×').Replace('X', '×').Split('×');
        if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out var width)
            || !int.TryParse(parts[1].Trim(), out var height)
            || width < 160 || width > 3840 || height < 120 || height > 2160
            || scanner.CameraIndex < 0 || scanner.CameraIndex > 20
            || scanner.CameraFrameRate < 1 || scanner.CameraFrameRate > 120
            || scanner.CameraScanTimeoutMilliseconds < 300 || scanner.CameraScanTimeoutMilliseconds > 15000)
        {
            ScannerTestMessage = "请检查相机参数：编号 0–20，分辨率 160×120 至 3840×2160，帧率 1–120，识码等待 300–15000 ms。";
            return false;
        }
        scanner.CameraWidth = width;
        scanner.CameraHeight = height;
        scanner.CameraAutoFocus = false;
        return true;
    }

    public string ScannerTestMessage
    {
        get => _scannerTestMessage;
        private set => SetField(ref _scannerTestMessage, value);
    }

    public string LowerMachineTestMessage
    {
        get => _lowerMachineTestMessage;
        private set => SetField(ref _lowerMachineTestMessage, value);
    }

    public ICommand TestScannerCommand { get; }

    public ICommand TestLowerMachineCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    private async Task TestScannerAsync()
    {
        if (!ApplyCameraSettings()) return;
        var result = await _communicationTestService.TestAsync(Configuration.Communication.Scanner);
        ScannerTestMessage = result.Message;
    }

    private async Task TestLowerMachineAsync()
    {
        var result = await _communicationTestService.TestAsync(Configuration.Communication.LowerMachine);
        LowerMachineTestMessage = result.IsSuccess && Configuration.Communication.LowerMachine.ConnectionType != ConnectionType.Disabled
            ? $"{result.Message}；这里只验证端口能打开。主界面连接时按选中的 IsOpen 或 Itemnum 方式握手，两者互斥；都未勾选时以端口打开为准。"
            : result.Message;
    }

    private static string[] GetSerialPortNames()
    {
        try
        {
            var assembly = System.Reflection.Assembly.Load("System.IO.Ports");
            var serialPortType = assembly.GetType("System.IO.Ports.SerialPort");
            var method = serialPortType?.GetMethod("GetPortNames", Type.EmptyTypes);
            return method?.Invoke(null, null) is string[] ports
                ? ports.OrderBy(port => port).ToArray()
                : Array.Empty<string>();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
