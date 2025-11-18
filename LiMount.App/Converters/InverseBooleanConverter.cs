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
    /// <summary>
    /// Inverts a boolean input for data binding conversions.
    /// </summary>
    /// <param name="value">The value to invert; expected to be a <see cref="bool"/>.</param>
    /// <returns>`true` if the input is `false` or not a boolean; `false` if the input is `true`.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    /// <summary>
    /// Inverts a boolean when converting from target to source during binding.
    /// </summary>
    /// <returns>`true` if <paramref name="value"/> is not a boolean; otherwise the logical negation of the boolean input.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }
}