using System.Windows.Input;
using BW_V01.App.Commands;
using BW_V01.Core.Configuration;

namespace BW_V01.App.ViewModels;

public sealed class UserConfigurationViewModel : DialogViewModelBase
{
    public UserConfigurationViewModel(UserConfiguration configuration)
    {
        Configuration = configuration;
        SaveCommand = new RelayCommand(() => Close(true));
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public UserConfiguration Configuration { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }
}
