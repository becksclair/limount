using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace LiMount.WinUI.Converters;

/// <summary>
/// Maps a boolean success flag to a success/error brush.
/// </summary>
public sealed partial class SuccessToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var success = value is bool b && b;
        var resources = Application.Current.Resources;
        var key = success ? "SuccessBrush" : "ErrorBrush";
        return resources[key] as SolidColorBrush ?? new SolidColorBrush(success ? Microsoft.UI.Colors.Green : Microsoft.UI.Colors.Red);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is SolidColorBrush brush &&
               brush.Color == ((SolidColorBrush?)Application.Current.Resources["SuccessBrush"])?.Color;
    }
}
