using System.Windows;

using Microsoft.Win32;
using CH_V01.App.ViewModels;

namespace CH_V01.App.Views;

public partial class BasicConfigurationWindow : Window
{
    public BasicConfigurationWindow()
    {
        InitializeComponent();
    }

    private void SelectLogDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(viewModel => path => viewModel.Configuration.Output.LogDirectory = path);

    private void SelectDclDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(viewModel => path => viewModel.Configuration.Output.DclDirectory = path);

    private void SelectCsvDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(viewModel => path => viewModel.Configuration.Output.CsvDirectory = path);

    private void SelectCustomerPathRootDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(viewModel => path => viewModel.Configuration.Output.CustomerPathRootDirectory = path);

    private void SelectSinglePieceCsvDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(viewModel => path => viewModel.Configuration.Output.SinglePieceCsvDirectory = path);

    private void SelectDirectory(Func<BasicConfigurationViewModel, Action<string>> getSetter)
    {
        if (DataContext is not BasicConfigurationViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFolderDialog { Title = "选择保存文件夹" };
        if (dialog.ShowDialog(this) == true)
        {
            getSetter(viewModel)(dialog.FolderName);
        }
    }
}
