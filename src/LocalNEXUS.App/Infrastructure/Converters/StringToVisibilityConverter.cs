using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LocalNEXUS.App.Infrastructure.Converters;

/// <summary>
/// Collapses an element when the bound string is null or empty. Pass <c>Invert</c> as the
/// converter parameter to collapse when the string has content instead.
/// </summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasContent = !string.IsNullOrWhiteSpace(value as string);
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);

        return hasContent != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("StringToVisibilityConverter is a one way converter.");
}
