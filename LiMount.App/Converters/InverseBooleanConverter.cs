using System.Globalization;
using System.Windows.Data;

namespace LiMount.App.Converters;

/// <summary>
/// Converts a boolean value to its inverse (true → false, false → true).
/// Used for enabling/disabling controls when IsBusy is true.
/// </summary>
[ValueConversion(typeof(bool), typeof(bool))]
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }
}
