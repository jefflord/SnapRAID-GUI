namespace SnapRAIDGUI.Converters;

using System.Globalization;
using System.Windows.Data;

[ValueConversion(typeof(double), typeof(double))]
public class PercentToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double pct && targetType == typeof(double))
            return Math.Max(0, Math.Min(100, pct));
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
