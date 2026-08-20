using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LocalNEXUS.App.Infrastructure.Converters;

/// <summary>
/// Shows an element while the bound value is true. Pass <c>Invert</c> as the converter parameter
/// to show it while the value is false instead.
/// </summary>
/// <remarks>
/// This replaces the framework converter of the same name, which has no way to invert and so
/// makes every "hide this while that is true" case reach for a second property on the view model
/// whose only job is to be the opposite of the first one.
/// </remarks>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);

        return flag != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("BooleanToVisibilityConverter is a one way converter.");
}
