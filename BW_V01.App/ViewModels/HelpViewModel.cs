using System.Windows.Input;
using BW_V01.App.Commands;

namespace BW_V01.App.ViewModels;

public sealed class HelpViewModel : DialogViewModelBase
{
    public HelpViewModel(string productName, string customerName, string mesPlugin)
    {
        ProductName = productName;
        CustomerName = customerName;
        MesPlugin = mesPlugin;
        CloseCommand = new RelayCommand(() => Close(true));
    }

    public string ProductName { get; }

    public string CustomerName { get; }

    public string MesPlugin { get; }

    public string Version => "BW_V01 第一版融合框架";

    public ICommand CloseCommand { get; }
}
