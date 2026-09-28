using System.Windows.Input;
using CH_V03.App.Commands;
using CH_V03.Core.Configuration;

namespace CH_V03.App.ViewModels;

public sealed class MesConfigurationViewModel : DialogViewModelBase
{
    public MesConfigurationViewModel(AppConfiguration configuration)
    {
        Configuration = configuration;
        SaveCommand = new RelayCommand(() => Close(true));
        CancelCommand = new RelayCommand(() => Close(false));
    }

    public AppConfiguration Configuration { get; }

    public ICommand SaveCommand { get; }

    public ICommand CancelCommand { get; }
}
