namespace SnapRAIDGUI.Converters;

using System.Globalization;
using System.Windows;
using System.Windows.Data;

/// <summary>
/// Converts bool to FontWeight: true -> Bold, false -> Normal.
/// </summary>
[ValueConversion(typeof(bool), typeof(FontWeight))]
public class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? FontWeights.SemiBold : FontWeights.Normal;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Like BooleanToVisibilityConverter but supports ConverterParameter=inverse to flip visibility.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class InvertibleBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (parameter?.ToString() == "inverse") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
