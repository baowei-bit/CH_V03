using System.Windows;
using BW_V01.App.ViewModels;

namespace BW_V01.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var mainWindow = new MainWindow();
        if (mainWindow.DataContext is not MainWindowViewModel viewModel || !viewModel.ShowLogin())
        {
            Shutdown();
            return;
        }

        MainWindow = mainWindow;
        mainWindow.Show();
    }
}
