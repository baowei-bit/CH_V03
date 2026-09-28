using System.Windows;
using CH_V03.App.Airtightness;

namespace CH_V03.App.Views;

public partial class AirtightnessHistoryWindow : Window
{
    private readonly AirtightnessViewModel _model;
    public AirtightnessHistoryWindow(AirtightnessViewModel model)
    {
        _model = model;
        InitializeComponent();
        DataContext = model;
    }
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try { _model.Refresh(); }
        catch (Exception exception)
        {
            _model.AddLog($"读取气密记录失败：{exception.Message}", "错误");
            MessageBox.Show(this, exception.Message, "读取气密记录失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
