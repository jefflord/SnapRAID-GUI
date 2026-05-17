namespace SnapRAIDGUI.Converters;

using System.Globalization;
using System.Windows.Data;

public class BytesToGBConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            var gb = bytes / (1024.0 * 1024.0 * 1024.0);
            return $"{gb:F1}";
        }
        return "0.0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
