using System.Collections.ObjectModel;
using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.Core.Configuration;

namespace CH_V03.App.ViewModels;

public sealed class ProjectProfileImportViewModel : DialogViewModelBase
{
    public ProjectProfileImportViewModel(IEnumerable<ProjectProfile> profiles)
    {
        Profiles = new ObservableCollection<ProjectProfileItemViewModel>(
            profiles.Select(profile => new ProjectProfileItemViewModel(profile)));
        SelectedProfile = Profiles.FirstOrDefault();
        ImportCommand = new RelayCommand(Import, () => SelectedProfile is not null);
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public ObservableCollection<ProjectProfileItemViewModel> Profiles { get; }

    private ProjectProfileItemViewModel? _selectedProfile;

    public ProjectProfileItemViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetField(ref _selectedProfile, value) && ImportCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public ICommand ImportCommand { get; }

    public ICommand CancelCommand { get; }

    private void Import()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        Close(true);
    }
}

public sealed class ProjectProfileItemViewModel
{
    public ProjectProfileItemViewModel(ProjectProfile profile)
    {
        Profile = profile;
    }

    public ProjectProfile Profile { get; }

    public string Title => string.IsNullOrWhiteSpace(Profile.Name) ? Profile.Id : Profile.Name;

    public string Subtitle => string.Join(" / ", new[]
    {
        Profile.CustomerName,
        Profile.ProductName,
        Profile.ProtocolAdapterId
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string Notes => Profile.Notes;

    public string MesPlugin => Profile.Configuration.Mes.SelectedPlugin;

    public string ExportPlugins => string.Join(", ", Profile.Configuration.Export.EnabledPluginIds);

    public string Scanner => FormatDevice(Profile.Configuration.Communication.Scanner);

    public string LowerMachine => FormatDevice(Profile.Configuration.Communication.LowerMachine);

    private static string FormatDevice(DeviceConnectionConfiguration configuration)
    {
        return configuration.ConnectionType switch
        {
            ConnectionType.SerialPort => $"{configuration.PortName} / {configuration.BaudRate}",
            ConnectionType.TcpClient or ConnectionType.TcpServer => $"{configuration.IpAddress}:{configuration.Port}",
            ConnectionType.UsbPosHid => "USB POS HID",
            ConnectionType.Disabled => "已禁用",
            _ => configuration.ConnectionType.ToString()
        };
    }
}
