using Microsoft.UI.Xaml.Data;

namespace LiMount.WinUI.Converters;

/// <summary>
/// Inverts a boolean value for XAML bindings.
/// </summary>
public sealed partial class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b ? !b : true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is bool b ? !b : true;
    }
}
