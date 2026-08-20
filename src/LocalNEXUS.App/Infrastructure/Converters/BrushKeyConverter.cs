using System.Globalization;
using System.Windows;
using System.Windows.Data;
using LocalNEXUS.App.Services.Theming;

namespace LocalNEXUS.App.Infrastructure.Converters;

/// <summary>
/// Resolves a brush from a resource key that the bound value already is.
/// </summary>
/// <remarks>
/// The sibling <see cref="ResourceLookupConverter"/> builds a key out of a fixed prefix and a
/// bound value, which is right when the view knows the category and the view model supplies the
/// state. This one is for the other case: the inspector shows a node, a model, a machine or a
/// coverage section in the same slot, and which category the colour comes from is part of what is
/// selected rather than part of where it is drawn.
/// </remarks>
public sealed class BrushKeyConverter : IValueConverter
{
    /// <summary>Resource key used when the bound key does not resolve.</summary>
    public string FallbackKey { get; set; } = "Accent.Neutral.Brush";

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;

        // From the live palette rather than the resources, for the same reason its sibling does:
        // the brush handed back has to be one that can still change colour when the theme does.
        return (string.IsNullOrEmpty(key) ? null : ThemePalette.Get(key))
               ?? ThemePalette.Get(FallbackKey)
               ?? (object?)DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("BrushKeyConverter is a one way converter.");
}
