using System.Windows;

using System.Windows.Controls;
using CH_V03.App.ViewModels;

namespace CH_V03.App.Views;

public partial class UserConfigurationWindow : Window
{
    public UserConfigurationWindow()
    {
        InitializeComponent();
    }

    private void TestPieceMode_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || DataContext is not UserConfigurationViewModel viewModel || !viewModel.ShowJiaLianYiSpecialModes
            || sender is not RadioButton { IsChecked: true })
        {
            return;
        }

        var prompt = new Window
        {
            Title = "生产模式验证",
            Owner = this,
            Width = 340,
            Height = 165,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel { Margin = new Thickness(18) }
        };
        var panel = (StackPanel)prompt.Content;
        panel.Children.Add(new TextBlock { Text = "切换到生产模式请输入密码：" });
        var passwordBox = new PasswordBox { Margin = new Thickness(0, 8, 0, 12) };
        panel.Children.Add(passwordBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var confirm = new Button { Content = "确定", Width = 72, IsDefault = true };
        confirm.Click += (_, _) => prompt.DialogResult = true;
        var cancel = new Button { Content = "取消", Width = 72, Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        if (prompt.ShowDialog() == true && passwordBox.Password == "123")
        {
            return;
        }

        viewModel.IsStandardPieceMode = true;
        if (prompt.DialogResult == true)
        {
            MessageBox.Show(this, CH_V03.App.Localization.UiLanguage.Text("密码错误，已保留标准片模式。"), CH_V03.App.Localization.UiLanguage.Text("生产模式验证"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
