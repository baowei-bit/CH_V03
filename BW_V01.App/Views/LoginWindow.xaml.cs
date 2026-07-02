using System.Windows;
using System.Windows.Interop;

namespace BW_V01.App.Views;

public partial class LoginWindow : Window
{
    private const int WmNcLeftButtonDoubleClick = 0x00A3;
    private const int HitTestSystemMenu = 3;

    public LoginWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(WindowMessageHook);
        }
    }

    private static IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmNcLeftButtonDoubleClick && wParam.ToInt32() == HitTestSystemMenu)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }
}
