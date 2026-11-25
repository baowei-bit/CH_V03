using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.Core.Configuration;

namespace CH_V03.App.ViewModels;

public sealed class UserConfigurationViewModel : DialogViewModelBase
{
    public UserConfigurationViewModel(UserConfiguration configuration, CompatibilityConfiguration compatibility, bool canManageAccounts)
    {
        Configuration = configuration;
        Compatibility = compatibility;
        CanManageAccounts = canManageAccounts;
        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public UserConfiguration Configuration { get; }

    public CompatibilityConfiguration Compatibility { get; }

    public bool CanManageAccounts { get; }

    public bool ShowJiaLianYiSpecialModes => Compatibility.EnableKunshanJiaLianYiDedicatedConfiguration;

    public bool IsStandardPieceMode
    {
        get => ShowJiaLianYiSpecialModes && Configuration.Jly.RunMode == JiaLianYiRunMode.StandardPiece;
        set
        {
            if (value && ShowJiaLianYiSpecialModes)
            {
                Configuration.Jly.RunMode = JiaLianYiRunMode.StandardPiece;
                NotifyJiaLianYiModeChanged();
            }
        }
    }

    public bool IsTestPieceMode
    {
        get => ShowJiaLianYiSpecialModes && Configuration.Jly.RunMode == JiaLianYiRunMode.TestPiece;
        set
        {
            if (value && ShowJiaLianYiSpecialModes)
            {
                Configuration.Jly.RunMode = JiaLianYiRunMode.TestPiece;
                NotifyJiaLianYiModeChanged();
            }
        }
    }

    public bool IsDebugMode
    {
        get => ShowJiaLianYiSpecialModes && Configuration.Jly.RunMode == JiaLianYiRunMode.Debug;
        set
        {
            if (value && ShowJiaLianYiSpecialModes)
            {
                Configuration.Jly.RunMode = JiaLianYiRunMode.Debug;
                NotifyJiaLianYiModeChanged();
            }
        }
    }

    public bool IsJingWangAccount { get; init; }

    public bool IsKeyboardSnMode
    {
        get => IsJingWangAccount && Configuration.ManualScanMode && Configuration.JingWangKeyboardSn;
        set
        {
            if (!value) return;
            Configuration.AutoScan = Configuration.NoScanMode = Configuration.ScanAheadEnabled = false;
            Configuration.ManualScanMode = Configuration.JingWangKeyboardSn = true;
            NotifyScanModeChanged();
        }
    }

    public bool IsAutoScanMode
    {
        get => Configuration.AutoScan;
        set
        {
            if (!value)
            {
                return;
            }

            Configuration.JingWangKeyboardSn = false;
            Configuration.AutoScan = true;
            Configuration.ManualScanMode = false;
            Configuration.NoScanMode = false;
            Configuration.ScanAheadEnabled = false;
            NotifyScanModeChanged();
        }
    }

    public bool IsManualScanMode
    {
        get => Configuration.ManualScanMode && !IsKeyboardSnMode;
        set
        {
            if (!value)
            {
                return;
            }

            Configuration.JingWangKeyboardSn = false;
            Configuration.AutoScan = false;
            Configuration.ManualScanMode = true;
            Configuration.NoScanMode = false;
            NotifyScanModeChanged();
        }
    }

    public bool IsNoScanMode
    {
        get => Configuration.NoScanMode;
        set
        {
            if (!value)
            {
                return;
            }

            Configuration.JingWangKeyboardSn = false;
            Configuration.AutoScan = false;
            Configuration.ManualScanMode = false;
            Configuration.NoScanMode = true;
            Configuration.ScanAheadEnabled = false;
            NotifyScanModeChanged();
        }
    }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }

    private void Save()
    {
        if (!Configuration.AutoScan && !Configuration.ManualScanMode && !Configuration.NoScanMode)
        {
            Configuration.JingWangKeyboardSn = false;
            Configuration.AutoScan = true;
        }

        if (!Configuration.ManualScanMode)
        {
            Configuration.ScanAheadEnabled = false;
        }

        if (ShowJiaLianYiSpecialModes && !double.IsFinite(Configuration.Jly.YieldUploadThreshold))
        {
            Configuration.Jly.YieldUploadThreshold = 90;
        }
        else if (ShowJiaLianYiSpecialModes)
        {
            Configuration.Jly.YieldUploadThreshold = Math.Clamp(Configuration.Jly.YieldUploadThreshold, 0, 100);
        }

        Close(true);
    }

    private void NotifyScanModeChanged()
    {
        OnPropertyChanged(nameof(IsKeyboardSnMode));
        OnPropertyChanged(nameof(IsAutoScanMode));
        OnPropertyChanged(nameof(IsManualScanMode));
        OnPropertyChanged(nameof(IsNoScanMode));
    }

    private void NotifyJiaLianYiModeChanged()
    {
        OnPropertyChanged(nameof(IsStandardPieceMode));
        OnPropertyChanged(nameof(IsTestPieceMode));
        OnPropertyChanged(nameof(IsDebugMode));
    }
}
