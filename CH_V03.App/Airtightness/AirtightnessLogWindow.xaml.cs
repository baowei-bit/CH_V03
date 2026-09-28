using System.Windows;
using CH_V03.App.Airtightness;

namespace CH_V03.App.Views;

public partial class AirtightnessLogWindow : Window
{
    public AirtightnessLogWindow(AirtightnessViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}
