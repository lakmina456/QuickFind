using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickFind.Views;

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}

/// <summary>Visible when an int/collection count is greater than 1 (e.g. tab close buttons).</summary>
public sealed class GreaterThanOneToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var n = value switch
        {
            int i => i,
            long l => (int)l,
            System.Collections.ICollection c => c.Count,
            _ => 0
        };
        return n > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Maps 0–1 progress to a pixel width (ConverterParameter = max width).</summary>
public sealed class ProgressWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var max = 84.0;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            max = p;
        var frac = value is double d ? d : value is float f ? f : 0.0;
        return Math.Clamp(frac, 0, 1) * max;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
