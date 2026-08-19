using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LocalNEXUS.App.Infrastructure.Converters;

/// <summary>
/// Binds a radio button to one member of an enum property. The converter parameter names
/// the member this button represents.
/// </summary>
public sealed class EnumToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is not string expected)
        {
            return false;
        }

        return string.Equals(value.ToString(), expected, StringComparison.Ordinal);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Only the button being switched on should write back; the one being switched off is ignored.
        if (value is not true || parameter is not string expected)
        {
            return DependencyProperty.UnsetValue;
        }

        var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (!enumType.IsEnum)
        {
            return DependencyProperty.UnsetValue;
        }

        return Enum.TryParse(enumType, expected, ignoreCase: false, out var parsed)
            ? parsed
            : DependencyProperty.UnsetValue;
    }
}
