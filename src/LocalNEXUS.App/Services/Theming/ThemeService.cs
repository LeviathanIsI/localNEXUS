using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Theming;

/// <summary>
/// Owns which theme the application is wearing, and swaps it without a restart.
/// </summary>
/// <remarks>
/// A theme is a dictionary of colours and nothing else. <see cref="SemanticBrushes"/> says which
/// colour each brush takes, and this puts the two together.
///
/// Applying a theme repaints the existing brush objects in place rather than replacing them, and
/// that is the whole design. Several brushes reach the screen through a converter that resolves
/// them once during a layout pass and is never asked again, so handing out new brush objects would
/// leave every node state, coverage bar and feed dot wearing the previous palette until something
/// unrelated happened to invalidate it. Repainting in place means an element does not even have to
/// have asked dynamically: the brush it is already holding simply changes colour.
///
/// The brushes are built here rather than declared in a resource dictionary, which is the obvious
/// way to write it and does not work. A dictionary of brushes whose colours are dynamic references
/// into the theme resolves each of those references once, when the brush is first created, and
/// never again, so a theme change moves nothing that is already on screen. Reading the new colours
/// by loading a second copy of that dictionary does not help either, because a dictionary loaded
/// from a uri is cached and the second copy is the live one.
/// </remarks>
public sealed partial class ThemeService : ObservableObject
{
    private readonly AppConfig _config;
    private readonly ResourceDictionary _applicationResources;

    /// <summary>The theme currently applied.</summary>
    [ObservableProperty]
    private AppTheme _current;

    public ThemeService(AppConfig config, ResourceDictionary applicationResources)
    {
        _config = config;
        _applicationResources = applicationResources;
        _current = config.Theme;
    }

    /// <summary>Every theme that can be picked, in the order the picker shows them.</summary>
    public static IReadOnlyList<ThemeDefinition> Available { get; } = new[]
    {
        new ThemeDefinition(
            AppTheme.VsCodeDark,
            "VS Code Dark+",
            "The reference palette this interface was designed against.",
            "Views/Themes/VsCodeDark.xaml"),
        new ThemeDefinition(
            AppTheme.DeepSlate,
            "Deep slate",
            "Cool and very dark, with a bright blue accent.",
            "Views/Themes/DeepSlate.xaml"),
        new ThemeDefinition(
            AppTheme.WarmCharcoal,
            "Warm charcoal",
            "Softer contrast than the others, which suits a long session.",
            "Views/Themes/WarmCharcoal.xaml"),
        new ThemeDefinition(
            AppTheme.NearBlack,
            "Near black",
            "The highest contrast of the dark themes, with a violet accent.",
            "Views/Themes/NearBlack.xaml"),
        new ThemeDefinition(
            AppTheme.Light,
            "Light",
            "Every state colour chosen for a light background rather than inverted from a dark one.",
            "Views/Themes/Light.xaml")
    };

    /// <summary>The definition of the theme currently applied.</summary>
    public ThemeDefinition CurrentDefinition => Definition(Current);

    /// <summary>Looks up a definition, falling back to the reference theme for a value this build does not know.</summary>
    public static ThemeDefinition Definition(AppTheme theme)
        => Available.FirstOrDefault(t => t.Theme == theme) ?? Available[0];

    /// <summary>Applies a theme and remembers it for the next session.</summary>
    public void Apply(AppTheme theme)
    {
        Swap(theme);

        Current = theme;

        if (_config.Theme == theme)
        {
            return;
        }

        _config.Theme = theme;
        _config.Save();
    }

    /// <summary>
    /// Applies the theme the configuration asked for, at startup. Separate from
    /// <see cref="Apply"/> because there is nothing to save and nothing has been painted yet.
    /// </summary>
    public void ApplySaved() => Swap(Current);

    /// <summary>
    /// Reads the theme's colours and paints every semantic brush with them, creating the brushes
    /// on the first call and only changing their colour on every call after that.
    /// </summary>
    private void Swap(AppTheme theme)
    {
        var definition = Definition(theme);
        var colours = (ResourceDictionary)Application.LoadComponent(definition.Uri);

        foreach (var (brushKey, colourKey) in SemanticBrushes.Map)
        {
            if (colours[colourKey] is not Color colour)
            {
                // A theme missing a colour is a mistake in that theme rather than something to
                // paper over, and leaving the brush as it was makes it visible without making the
                // window unreadable.
                continue;
            }

            // The live copy, repainted in place, which is what a converter result keeps pointing at.
            ThemePalette.Set(brushKey, colour);

            // And a fresh copy for the resource dictionary, which every dynamic reference in the
            // XAML re-resolves to. It has to be a new object rather than the one above, because
            // anything placed in the application resources is frozen on the way in.
            _applicationResources[brushKey] = new SolidColorBrush(colour);
        }
    }
}
