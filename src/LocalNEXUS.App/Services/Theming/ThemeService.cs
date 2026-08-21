using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
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

    /// <summary>
    /// The least opaque the window's base layer may be made.
    /// </summary>
    /// <remarks>
    /// A floor rather than a range down to nothing, because the thing behind the window is
    /// arbitrary and a base layer thin enough to read a document through is a base layer that
    /// cannot be read itself. This is the point where the palette still wins over whatever is
    /// underneath it.
    /// </remarks>
    public const double MinimumWindowOpacity = 0.55d;

    /// <summary>The theme currently applied.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentDefinition))]
    [NotifyPropertyChangedFor(nameof(IsTransparencyAvailable))]
    [NotifyPropertyChangedFor(nameof(IsWindowTranslucent))]
    [NotifyPropertyChangedFor(nameof(EffectiveWindowOpacity))]
    [NotifyPropertyChangedFor(nameof(TransparencySummary))]
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
            AppTheme.EditorDark,
            "Editor dark",
            "Familiar if you live in an editor.",
            "Views/Themes/EditorDark.xaml"),
        new ThemeDefinition(
            AppTheme.DeepSlate,
            "Deep slate",
            "Cool blue-black, bright accent.",
            "Views/Themes/DeepSlate.xaml"),
        new ThemeDefinition(
            AppTheme.WarmCharcoal,
            "Warm charcoal",
            "Easiest on the eyes for long sessions.",
            "Views/Themes/WarmCharcoal.xaml"),
        new ThemeDefinition(
            AppTheme.NearBlack,
            "Near black",
            "Highest contrast, violet accent.",
            "Views/Themes/NearBlack.xaml"),
        new ThemeDefinition(
            AppTheme.Light,
            "Light",
            "For bright rooms.",
            "Views/Themes/Light.xaml"),
        new ThemeDefinition(
            AppTheme.Mystic,
            "Mystic",
            "Violet wash, and the only one you can see through.",
            "Views/Themes/Mystic.xaml",
            ThemeCapabilities.GradientSurface | ThemeCapabilities.WindowTransparency)
    };

    /// <summary>The definition of the theme currently applied.</summary>
    public ThemeDefinition CurrentDefinition => Definition(Current);

    /// <summary>
    /// True when the transparency control has anything to offer.
    /// </summary>
    /// <remarks>
    /// Two questions, and neither of them is which theme this is. The theme has to declare the
    /// capability, and the machine has to be able to honour it, which is a real question because
    /// the backdrop arrived part way through Windows 11.
    /// </remarks>
    public bool IsTransparencyAvailable
        => CurrentDefinition.SupportsTransparency && WindowBackdrop.IsSupported;

    /// <summary>What the appearance panel says about transparency under this theme.</summary>
    public string TransparencySummary
    {
        get
        {
            if (!CurrentDefinition.SupportsTransparency)
            {
                return $"{CurrentDefinition.DisplayName} is opaque. Pick a theme that can be seen through to set this.";
            }

            return WindowBackdrop.IsSupported
                ? "How solid the window is. What is behind the application shows through the base layer, and the panels stay opaque."
                : "This needs Windows 11 22H2 or later, which this machine does not have. The window stays opaque.";
        }
    }

    /// <summary>
    /// How opaque the window's base layer should be, from the floor to fully solid.
    /// </summary>
    public double WindowOpacity
    {
        get => Math.Clamp(_config.WindowOpacity, MinimumWindowOpacity, 1d);
        set
        {
            var clamped = Math.Clamp(value, MinimumWindowOpacity, 1d);

            if (Math.Abs(_config.WindowOpacity - clamped) < 0.001d)
            {
                return;
            }

            _config.WindowOpacity = clamped;
            _config.Save();

            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveWindowOpacity));
            OnPropertyChanged(nameof(IsWindowTranslucent));
        }
    }

    /// <summary>
    /// What the base layer is actually painted at, which is solid unless the theme in force offers
    /// transparency and this machine can do it.
    /// </summary>
    public double EffectiveWindowOpacity => IsTransparencyAvailable ? WindowOpacity : 1d;

    /// <summary>True when the window should be asking the compositor for a backdrop.</summary>
    /// <remarks>
    /// Not the same question as whether transparency is available: a theme that offers it, set to
    /// fully solid, has nothing to show through and should not be paying for a live blur.
    /// </remarks>
    public bool IsWindowTranslucent => IsTransparencyAvailable && WindowOpacity < 1d;

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

        foreach (var (brushKey, colourKeys) in SemanticBrushes.Gradients)
        {
            var stops = new List<Color>(colourKeys.Count);

            foreach (var colourKey in colourKeys)
            {
                if (colours[colourKey] is Color stop)
                {
                    stops.Add(stop);
                }
            }

            if (stops.Count != colourKeys.Count)
            {
                // Same rule as a missing solid colour: a theme short of a stop is a mistake in
                // that theme, and leaving the gradient as it was makes it visible without leaving
                // a hole where the window used to be.
                continue;
            }

            ThemePalette.SetGradient(brushKey, stops);
            _applicationResources[brushKey] = ThemePalette.BuildGradient(stops);
        }
    }
}
