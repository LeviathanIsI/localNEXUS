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

    /// <summary>The live brush for a key, or null when this build has no such brush.</summary>
    public static SolidColorBrush? Get(string key)
        => Brushes.TryGetValue(key, out var brush) ? brush : null;

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
}
