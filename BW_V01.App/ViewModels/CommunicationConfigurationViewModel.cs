using System.Windows.Input;
using BW_V01.App.Commands;
using BW_V01.Core.Communication;
using BW_V01.Core.Configuration;

namespace BW_V01.App.ViewModels;

public sealed class CommunicationConfigurationViewModel : DialogViewModelBase
{
    private readonly CommunicationTestService _communicationTestService;
    private string _scannerTestMessage = "未测试";
    private string _lowerMachineTestMessage = "未测试";

    public CommunicationConfigurationViewModel(AppConfiguration configuration, CommunicationTestService communicationTestService)
    {
        Configuration = configuration;
        _communicationTestService = communicationTestService;
        TestScannerCommand = new AsyncRelayCommand(TestScannerAsync);
        TestLowerMachineCommand = new AsyncRelayCommand(TestLowerMachineAsync);
        SaveCommand = new RelayCommand(() => Close(true));
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public AppConfiguration Configuration { get; }

    public ConnectionType[] ConnectionTypes { get; } = Enum.GetValues<ConnectionType>();

    public string[] SerialPorts { get; } = GetSerialPortNames();

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
        var result = await _communicationTestService.TestAsync(Configuration.Communication.Scanner);
        ScannerTestMessage = result.Message;
    }

    private async Task TestLowerMachineAsync()
    {
        var result = await _communicationTestService.TestAsync(Configuration.Communication.LowerMachine);
        LowerMachineTestMessage = result.Message;
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
