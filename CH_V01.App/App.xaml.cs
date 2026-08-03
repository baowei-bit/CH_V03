using System.Windows;
using CH_V01.App.ViewModels;

namespace CH_V01.App;

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
