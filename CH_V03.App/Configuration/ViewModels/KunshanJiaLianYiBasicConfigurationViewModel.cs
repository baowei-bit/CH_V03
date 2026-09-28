using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.Core.Configuration;

namespace CH_V03.App.ViewModels;

public sealed class KunshanJiaLianYiBasicConfigurationViewModel : DialogViewModelBase
{
    private const string JiaLianYiExportPluginId = "JiaLianYi.Export";
    private readonly KunshanJiaLianYiRuntimeActions _runtimeActions;

    public KunshanJiaLianYiBasicConfigurationViewModel(AppConfiguration configuration, KunshanJiaLianYiRuntimeActions runtimeActions)
    {
        Configuration = configuration;
        _runtimeActions = runtimeActions;
        ToggleScannerCommand = new AsyncRelayCommand(ToggleScannerAsync);
        ToggleLowerMachineCommand = new AsyncRelayCommand(ToggleLowerMachineAsync);
        ToggleMesCommand = new AsyncRelayCommand(ToggleMesAsync);
        SelectStandardPieceModeCommand = new RelayCommand(() => SelectRunMode(JiaLianYiRunMode.StandardPiece));
        SelectTestPieceModeCommand = new RelayCommand(() => SelectRunMode(JiaLianYiRunMode.TestPiece));
        SelectDebugModeCommand = new RelayCommand(() => SelectRunMode(JiaLianYiRunMode.Debug));
        SaveCommand = new RelayCommand(() => Close(true));
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public AppConfiguration Configuration { get; }
    public string ScannerState => _runtimeActions.ScannerState();
    public string LowerMachineState => _runtimeActions.LowerMachineState();
    public string MesState => _runtimeActions.MesState();
    public string ItemCode
    {
        get => Configuration.Mes.PluginOptions.TryGetValue("ItemCode", out var itemCode)
            ? itemCode
            : GetExportOptions().GetValueOrDefault("ItemCode", string.Empty);
        set
        {
            var normalizedValue = value ?? string.Empty;
            Configuration.Mes.PluginOptions["ItemCode"] = normalizedValue;
            GetExportOptions()["ItemCode"] = normalizedValue;
        }
    }
    public bool CanSelectRunMode => Configuration.Mes.Enabled && Configuration.Mes.OnlineMode;
    public bool IsStandardPieceMode => Configuration.User.Jly.RunMode == JiaLianYiRunMode.StandardPiece;
    public bool IsTestPieceMode => Configuration.User.Jly.RunMode == JiaLianYiRunMode.TestPiece;
    public bool IsDebugMode => Configuration.User.Jly.RunMode == JiaLianYiRunMode.Debug;
    public ICommand ToggleScannerCommand { get; }
    public ICommand ToggleLowerMachineCommand { get; }
    public ICommand ToggleMesCommand { get; }
    public ICommand SelectStandardPieceModeCommand { get; }
    public ICommand SelectTestPieceModeCommand { get; }
    public ICommand SelectDebugModeCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task ToggleScannerAsync() { await _runtimeActions.ToggleScanner(); RefreshRuntimeStatus(); }
    private async Task ToggleLowerMachineAsync() { await _runtimeActions.ToggleLowerMachine(); RefreshRuntimeStatus(); }
    private async Task ToggleMesAsync()
    {
        await _runtimeActions.ToggleMes();
        Configuration.Mes.OnlineMode = _runtimeActions.IsMesOnline();
        RefreshRuntimeStatus();
    }

    private void SelectRunMode(JiaLianYiRunMode mode)
    {
        if (!CanSelectRunMode) return;
        Configuration.User.Jly.RunMode = mode;
        OnPropertyChanged(nameof(IsStandardPieceMode));
        OnPropertyChanged(nameof(IsTestPieceMode));
        OnPropertyChanged(nameof(IsDebugMode));
    }

    private void RefreshRuntimeStatus()
    {
        OnPropertyChanged(nameof(ScannerState));
        OnPropertyChanged(nameof(LowerMachineState));
        OnPropertyChanged(nameof(MesState));
        OnPropertyChanged(nameof(CanSelectRunMode));
    }

    private Dictionary<string, string> GetExportOptions()
    {
        if (!Configuration.Export.PluginOptions.TryGetValue(JiaLianYiExportPluginId, out var options))
        {
            options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Configuration.Export.PluginOptions[JiaLianYiExportPluginId] = options;
        }

        return options;
    }
}

public sealed class KunshanJiaLianYiRuntimeActions
{
    public required Func<Task> ToggleScanner { get; init; }
    public required Func<Task> ToggleLowerMachine { get; init; }
    public required Func<Task> ToggleMes { get; init; }
    public required Func<string> ScannerState { get; init; }
    public required Func<string> LowerMachineState { get; init; }
    public required Func<string> MesState { get; init; }
    public required Func<bool> IsMesOnline { get; init; }
}
