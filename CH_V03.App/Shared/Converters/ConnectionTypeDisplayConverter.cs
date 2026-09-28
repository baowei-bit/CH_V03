using System.Globalization;
using System.Windows.Data;
using CH_V03.Core.Configuration;

namespace CH_V03.App.Converters;

public sealed class ConnectionTypeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            ConnectionType.UsbPosHid => "USB POS HID",
            ConnectionType.CameraQr => "USB Video Device（摄像头扫码）",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
