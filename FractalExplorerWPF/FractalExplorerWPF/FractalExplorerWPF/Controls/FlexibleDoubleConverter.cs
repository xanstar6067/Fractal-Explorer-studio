using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FractalExplorerWPF.Controls;

/// <summary>Numeric editor binding with both decimal comma and decimal point, without digit grouping.</summary>
public sealed class FlexibleDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ((double)value).ToString("0.###", CultureInfo.InvariantCulture);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string text && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double result) && double.IsFinite(result)) return result;
        // WPF does not catch converter exceptions. UnsetValue produces a validation error on the field.
        return DependencyProperty.UnsetValue;
    }
}
