using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RoninDiskManager.Converters;

/// <summary>
/// Returns Visible when the bound bool is <c>false</c>, Collapsed when <c>true</c>.
/// Opposite of the built-in BooleanToVisibilityConverter.
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Collapsed;
}
