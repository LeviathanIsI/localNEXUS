using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LocalNEXUS.App.Infrastructure.Converters;

/// <summary>
/// Shows an element while a bound enum equals the member named by the converter parameter.
/// </summary>
/// <remarks>
/// The parameter may name several members separated by commas, and may be prefixed with an
/// exclamation mark to mean anything but those. That covers the two cases a state driven
/// interface actually has, "show this for Running and Paused" and "show this unless it is
/// Idle", without either becoming a boolean on the view model that only exists for the view.
/// </remarks>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string expected || expected.Length == 0)
        {
            return Visibility.Collapsed;
        }

        var invert = expected[0] == '!';
        if (invert)
        {
            expected = expected[1..];
        }

        var token = value?.ToString();
        var matches = expected
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(name => string.Equals(name, token, StringComparison.Ordinal));

        return matches != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("EnumToVisibilityConverter is a one way converter.");
}
