namespace SnapRAIDGUI.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

[ValueConversion(typeof(object), typeof(Visibility))]
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value != null;
        var inverse = parameter?.ToString() == "inverse";
        return isVisible ^ inverse ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
