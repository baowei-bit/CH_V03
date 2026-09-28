using System.Windows;

namespace CH_V03.App.Views;

public partial class CommunicationConfigurationWindow : Window
{
    public CommunicationConfigurationWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (DialogResult == true && HasValidationError(this))
            {
                e.Cancel = true;
                MessageBox.Show(this, "参数格式不正确，请修正标红的输入框后再保存。", "参数无效");
            }
        };
    }

    private static bool HasValidationError(DependencyObject node)
    {
        if (System.Windows.Controls.Validation.GetHasError(node)) return true;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            if (HasValidationError(System.Windows.Media.VisualTreeHelper.GetChild(node, i))) return true;
        return false;
    }
}
