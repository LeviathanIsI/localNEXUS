using System.Windows;
using System.Windows.Media;

namespace LocalNEXUS.App.Services.Theming;

/// <summary>
/// The live brushes, held outside the application resources so that they can change colour.
/// </summary>
/// <remarks>
/// This exists because of one WPF rule: a brush put into <c>Application.Resources</c> is frozen,
/// since those resources are reachable from any thread. Frozen means read only, so repainting one
/// in place throws, and the throw happens inside a binding write where the framework swallows it.
/// That is why a theme change appeared to do nothing at all the second time it was asked for.
///
/// So there are two copies of each brush and they update differently. The resource dictionary gets
/// a fresh brush per theme, which is what every <c>DynamicResource</c> in the XAML re-resolves to.
/// The copy here is never handed to a resource dictionary, is therefore never frozen, and is
/// repainted in place, which is what the converters need: a converter resolves a brush once during
/// a layout pass and is never asked again, so the object it handed out has to be the one that
/// changes colour.
///
/// Static because the converters are created by the XAML parser and have nothing injected into
/// them. It is written only by <see cref="ThemeService"/> and only on the UI thread.
/// </remarks>
public static class ThemePalette
{
    private static readonly Dictionary<string, SolidColorBrush> Brushes = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LinearGradientBrush> Gradients = new(StringComparer.Ordinal);

    /// <summary>
    /// Where the surface gradient runs, and where its middle stop sits.
    /// </summary>
    /// <remarks>
    /// Off the diagonal and weighted past halfway, which is the installer's geometry. It is here
    /// rather than in a theme because it is the shape of the gradient rather than its colour, and
    /// a theme supplies colours and nothing else.
    /// </remarks>
    private static readonly Point GradientStart = new(0d, 0d);

    private static readonly Point GradientEnd = new(0.85d, 1d);

    private static readonly double[] GradientOffsets = { 0d, 0.55d, 1d };

    /// <summary>The live brush for a key, or null when this build has no such brush.</summary>
    public static SolidColorBrush? Get(string key)
        => Brushes.TryGetValue(key, out var brush) ? brush : null;

    /// <summary>The live gradient for a key, or null when this build has no such gradient.</summary>
    public static LinearGradientBrush? GetGradient(string key)
        => Gradients.TryGetValue(key, out var brush) ? brush : null;

    /// <summary>
    /// Points a brush at a colour, creating it the first time and repainting it every time after.
    /// </summary>
    internal static SolidColorBrush Set(string key, Color colour)
    {
        if (Brushes.TryGetValue(key, out var existing))
        {
            existing.Color = colour;
            return existing;
        }

        var created = new SolidColorBrush(colour);
        Brushes[key] = created;
        return created;
    }

    /// <summary>
    /// Points a gradient at a set of colours, creating it the first time and repainting its stops
    /// every time after.
    /// </summary>
    /// <remarks>
    /// The same rule as a solid brush and for the same reason, one step further in. A gradient put
    /// into the application resources is frozen, and so are the stops inside it, so a theme change
    /// has to write into a copy that was never handed over. Repainting the stops rather than
    /// replacing the collection matters as well: an element holding this brush is notified through
    /// the brush it already has, and swapping the stop objects would leave it holding a gradient
    /// whose colours changed underneath a collection it is no longer watching.
    /// </remarks>
    internal static LinearGradientBrush SetGradient(string key, IReadOnlyList<Color> stops)
    {
        if (Gradients.TryGetValue(key, out var existing))
        {
            for (var i = 0; i < existing.GradientStops.Count && i < stops.Count; i++)
            {
                existing.GradientStops[i].Color = stops[i];
            }

            return existing;
        }

        var created = BuildGradient(stops);
        Gradients[key] = created;
        return created;
    }

    /// <summary>
    /// A fresh gradient over the same colours, for the resource dictionary to freeze.
    /// </summary>
    internal static LinearGradientBrush BuildGradient(IReadOnlyList<Color> stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = GradientStart,
            EndPoint = GradientEnd
        };

        for (var i = 0; i < stops.Count; i++)
        {
            var offset = i < GradientOffsets.Length
                ? GradientOffsets[i]
                : (double)i / Math.Max(1, stops.Count - 1);

            brush.GradientStops.Add(new GradientStop(stops[i], offset));
        }

        return brush;
    }
}
