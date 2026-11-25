using System;
using System.Windows;
using Microsoft.Win32;
using CH_V03.App.ViewModels;

namespace CH_V03.App.Views;

public partial class PluginConfigurationWindow : Window
{
    public PluginConfigurationWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncJingWangPassword();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is PluginConfigurationViewModel oldModel) oldModel.PropertyChanged -= JingWangModel_PropertyChanged;
            if (e.NewValue is PluginConfigurationViewModel newModel) newModel.PropertyChanged += JingWangModel_PropertyChanged;
            SyncJingWangPassword();
        };
    }

    private void JingWangModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PluginConfigurationViewModel.JingWangDbPassword) or nameof(PluginConfigurationViewModel.JingWangS3SecretKey)) SyncJingWangPassword();
    }

    private void SyncJingWangPassword()
    {
        if (DataContext is PluginConfigurationViewModel model && JingWangDbPasswordBox.Password != model.JingWangDbPassword)
            JingWangDbPasswordBox.Password = model.JingWangDbPassword;
        if (DataContext is PluginConfigurationViewModel vm && JingWangS3SecretKeyBox.Password != vm.JingWangS3SecretKey)
            JingWangS3SecretKeyBox.Password = vm.JingWangS3SecretKey;
    }

    private void JingWangDbPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is PluginConfigurationViewModel model && model.IsJingWangMesPlugin)
            model.JingWangDbPassword = JingWangDbPasswordBox.Password;
    }

    private void JingWangS3SecretKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is PluginConfigurationViewModel model && model.IsJingWangMesPlugin)
            model.JingWangS3SecretKey = JingWangS3SecretKeyBox.Password;
    }

    private void SelectDclDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(viewModel => path => viewModel.Configuration.Output.DclDirectory = path);
    private void SelectCsvDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(viewModel => path => viewModel.Configuration.Output.CsvDirectory = path);
    private void SelectCustomerPathRootDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(viewModel => path => viewModel.Configuration.Output.CustomerPathRootDirectory = path);
    private void SelectSinglePieceCsvDirectory_Click(object sender, RoutedEventArgs e) => SelectDirectory(viewModel => path => viewModel.Configuration.Output.SinglePieceCsvDirectory = path);

    private void SelectDirectory(Func<PluginConfigurationViewModel, Action<string>> getSetter)
    {
        if (DataContext is not PluginConfigurationViewModel viewModel)
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
