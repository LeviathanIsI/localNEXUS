using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Theming;

/// <summary>
/// Owns which theme the application is wearing, and swaps it without a restart.
/// </summary>
/// <remarks>
/// A theme is a dictionary of colours and nothing else. Applying one replaces that dictionary in
/// the application resources, and the brushes in Brushes.xaml, whose colours are dynamic
/// references into it, repaint in place.
///
/// Repainting in place rather than replacing the brushes is what makes the swap complete. Several
/// brushes reach the screen through a converter that resolves them once during a layout pass and
/// is never asked again, so a theme that handed out new brush objects would leave every node
/// state, coverage bar and feed dot wearing the previous palette until something happened to
/// invalidate it.
///
/// The colours are also copied straight onto the application itself, because a dynamic reference
/// resolves against the dictionary it can see and the brushes are declared in a sibling
/// dictionary rather than inside the theme. Copying makes the resolution unambiguous and costs
/// one pass over thirty keys.
/// </remarks>
public sealed partial class ThemeService : ObservableObject
{
    /// <summary>The key every theme dictionary defines, used to find the one currently applied.</summary>
    private const string ProbeKey = "Surface.WindowColor";

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
            "pack://application:,,,/Views/Themes/VsCodeDark.xaml"),
        new ThemeDefinition(
            AppTheme.DeepSlate,
            "Deep slate",
            "Cool and very dark, with a bright blue accent.",
            "pack://application:,,,/Views/Themes/DeepSlate.xaml"),
        new ThemeDefinition(
            AppTheme.WarmCharcoal,
            "Warm charcoal",
            "Softer contrast than the others, which suits a long session.",
            "pack://application:,,,/Views/Themes/WarmCharcoal.xaml"),
        new ThemeDefinition(
            AppTheme.NearBlack,
            "Near black",
            "The highest contrast of the dark themes, with a violet accent.",
            "pack://application:,,,/Views/Themes/NearBlack.xaml"),
        new ThemeDefinition(
            AppTheme.Light,
            "Light",
            "Every state colour chosen for a light background rather than inverted from a dark one.",
            "pack://application:,,,/Views/Themes/Light.xaml")
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
    /// Replaces the colour dictionary and copies its entries onto the application, so that a
    /// dynamic reference from any dictionary resolves to the new value.
    /// </summary>
    private void Swap(AppTheme theme)
    {
        var definition = Definition(theme);
        var loaded = new ResourceDictionary { Source = definition.Uri };

        var existing = _applicationResources.MergedDictionaries
            .FirstOrDefault(d => d.Contains(ProbeKey));

        if (existing is null)
        {
            // Nothing to replace means the shell has not merged a theme yet, which happens only
            // if App.xaml stopped shipping one. Merging is the recoverable answer.
            _applicationResources.MergedDictionaries.Insert(0, loaded);
        }
        else
        {
            var index = _applicationResources.MergedDictionaries.IndexOf(existing);
            _applicationResources.MergedDictionaries[index] = loaded;
        }

        foreach (var key in loaded.Keys)
        {
            if (loaded[key] is Color colour)
            {
                _applicationResources[key] = colour;
            }
        }
    }
}
