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
    /// Inverts a boolean input for use in data binding.
    /// </summary>
    /// <param name="value">The input value to invert; if not a <see cref="bool"/>, the converter returns <c>true</c>.</param>
    /// <param name="targetType">The type of the binding target (ignored).</param>
    /// <param name="parameter">An optional parameter (ignored).</param>
    /// <param name="culture">The culture to use in the converter (ignored).</param>
    /// <returns>`true` if the input is not a boolean or if the input boolean is `false`; `false` if the input boolean is `true`.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    /// <summary>
    /// Converts a bound target value back to the source value by inverting a boolean input.
    /// </summary>
    /// <param name="value">The value from the binding target; expected to be a boolean produced by the target.</param>
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