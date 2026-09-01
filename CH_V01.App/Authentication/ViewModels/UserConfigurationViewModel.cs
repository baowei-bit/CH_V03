using System.Windows.Input;
using CH_V01.App.Commands;
using CH_V01.Core.Configuration;

namespace CH_V01.App.ViewModels;

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

    public bool IsStandardPieceMode
    {
        get => Configuration.Jly.RunMode == JiaLianYiRunMode.StandardPiece;
        set
        {
            if (value)
            {
                Configuration.Jly.RunMode = JiaLianYiRunMode.StandardPiece;
                NotifyJiaLianYiModeChanged();
            }
        }
    }

    public bool IsTestPieceMode
    {
        get => Configuration.Jly.RunMode == JiaLianYiRunMode.TestPiece;
        set
        {
            if (value)
            {
                Configuration.Jly.RunMode = JiaLianYiRunMode.TestPiece;
                NotifyJiaLianYiModeChanged();
            }
        }
    }

    public bool IsDebugMode
    {
        get => Configuration.Jly.RunMode == JiaLianYiRunMode.Debug;
        set
        {
            if (value)
            {
                Configuration.Jly.RunMode = JiaLianYiRunMode.Debug;
                NotifyJiaLianYiModeChanged();
            }
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

            Configuration.AutoScan = true;
            Configuration.ManualScanMode = false;
            Configuration.NoScanMode = false;
            Configuration.ScanAheadEnabled = false;
            NotifyScanModeChanged();
        }
    }

    public bool IsManualScanMode
    {
        get => Configuration.ManualScanMode;
        set
        {
            if (!value)
            {
                return;
            }

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
            Configuration.AutoScan = true;
        }

        if (!Configuration.ManualScanMode)
        {
            Configuration.ScanAheadEnabled = false;
        }

        if (!double.IsFinite(Configuration.Jly.YieldUploadThreshold))
        {
            Configuration.Jly.YieldUploadThreshold = 90;
        }
        else
        {
            Configuration.Jly.YieldUploadThreshold = Math.Clamp(Configuration.Jly.YieldUploadThreshold, 0, 100);
        }

        Close(true);
    }

    private void NotifyScanModeChanged()
    {
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
