using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LocalNEXUS.App.Services.Theming;

namespace LocalNEXUS.App.Infrastructure.Converters;

/// <summary>
/// Resolves an application brush from the value being bound.
/// </summary>
/// <remarks>
/// The converter parameter is a category prefix and the key is built as
/// <c>{prefix}.{value}.Brush</c>. Binding a <see cref="Models.PinType"/> of <c>Code</c> with the
/// parameter <c>Pin</c> therefore resolves <c>Pin.Code.Brush</c>. Every colour stays in the theme
/// dictionary rather than in converter code, and one converter covers pin types, node states,
/// node types, activity kinds and plain booleans.
/// </remarks>
public sealed class ResourceLookupConverter : IValueConverter
{
    /// <summary>Suffix appended to every generated key.</summary>
    public string Suffix { get; set; } = "Brush";

    /// <summary>Resource key used when the generated key does not resolve.</summary>
    public string FallbackKey { get; set; } = "Accent.Neutral.Brush";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string prefix || string.IsNullOrEmpty(prefix))
        {
            return DependencyProperty.UnsetValue;
        }

        var token = value?.ToString() ?? "Null";
        var key = $"{prefix}.{token}.{Suffix}";

        // From the live palette rather than the resources, because a converter is asked once and
        // the brush it hands back has to be one that can still change colour when the theme does.
        return ThemePalette.Get(key)
               ?? ThemePalette.Get(FallbackKey)
               ?? (object?)DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("ResourceLookupConverter is a one way converter.");
}
