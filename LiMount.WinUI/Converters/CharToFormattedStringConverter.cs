using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace LiMount.WinUI.Converters;

public sealed partial class CharToFormattedStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is char c ? $"{c}:" : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        var str = value?.ToString()?.TrimEnd(':');
        if (str?.Length == 1)
            return str[0];
        return DependencyProperty.UnsetValue;
    }
}
