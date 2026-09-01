using System.Globalization;
using System.Windows.Data;
using CH_V01.Core.Configuration;

namespace CH_V01.App.Converters;

public sealed class ConnectionTypeDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is ConnectionType.UsbPosHid ? "USB POS HID" : value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
