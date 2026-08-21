using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LocalNEXUS.Installer.Infrastructure;

/// <summary>Shows an element while a boolean is true. Pass Invert to reverse it.</summary>
public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);

        return flag != invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One way only.");
}

/// <summary>Shows an element while an enum equals the parameter.</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null
           && parameter is string name
           && string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One way only.");
}

/// <summary>True while an enum equals the parameter, for a radio style binding.</summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null
           && parameter is string name
           && string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true && parameter is string name && targetType.IsEnum
            ? Enum.Parse(targetType, name)
            : Binding.DoNothing;
}

/// <summary>
/// Turns a fraction into a width, so the progress sliver and the install bar can be drawn as a
/// plain border rather than a templated control.
/// </summary>
public sealed class FractionToWidthConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double fraction || values[1] is not double available)
        {
            return 0d;
        }

        if (double.IsNaN(available) || double.IsInfinity(available) || available <= 0d)
        {
            return 0d;
        }

        return Math.Clamp(fraction, 0d, 1d) * available;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One way only.");
}

/// <summary>
/// Builds a rounded rectangle the size of the element, for clipping children to a rounded window.
/// </summary>
/// <remarks>
/// A Border with a CornerRadius rounds its own background and nothing else, so a child painting
/// its own fill squares the corner off again. Clipping the content to the same shape is the fix,
/// and the geometry has to be built from the live size rather than written down, or it is wrong
/// on every display that is not scaled at one hundred percent.
/// </remarks>
public sealed class RoundedClipConverter : IMultiValueConverter
{
    public object Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double width || values[1] is not double height)
        {
            return System.Windows.Media.Geometry.Empty;
        }

        if (double.IsNaN(width) || double.IsNaN(height) || width <= 0d || height <= 0d)
        {
            return System.Windows.Media.Geometry.Empty;
        }

        var radius = parameter is string text && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 12d;

        return new System.Windows.Media.RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("One way only.");
}
