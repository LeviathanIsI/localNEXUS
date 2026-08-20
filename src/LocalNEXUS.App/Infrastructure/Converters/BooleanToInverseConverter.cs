using System.Globalization;
using System.Windows.Data;

namespace LocalNEXUS.App.Infrastructure.Converters;

/// <summary>
/// Flips a boolean, for the properties WPF exposes the opposite way round from the view model.
/// </summary>
/// <remarks>
/// The case this exists for is <c>IsReadOnly</c>, which is the negation of every "can this be
/// edited" flag a view model would sensibly have. Inverting here is better than adding a second
/// property whose only job is to be the opposite of the first one.
/// </remarks>
public sealed class BooleanToInverseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}
