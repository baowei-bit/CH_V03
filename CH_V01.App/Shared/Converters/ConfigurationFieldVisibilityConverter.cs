using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CH_V01.App.Converters;

public sealed class ConfigurationFieldVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fieldKey = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(fieldKey))
        {
            return Visibility.Visible;
        }

        var visibleFields = value as IEnumerable<string>;
        if (visibleFields is null || !visibleFields.Any())
        {
            return Visibility.Visible;
        }

        var requestedKeys = fieldKey
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return requestedKeys.Any(requestedKey => visibleFields.Contains(requestedKey, StringComparer.OrdinalIgnoreCase))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
