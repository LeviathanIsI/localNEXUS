namespace LocalNEXUS.App.Services.Theming;

/// <summary>
/// What a theme can do beyond supplying colours.
/// </summary>
/// <remarks>
/// Themes were a flat set of brushes and nothing else until one of them wanted a window that is
/// not fully opaque. The alternative was a check for one theme by name wherever that mattered,
/// which puts the same condition in several files and makes a second theme with the same idea a
/// second round of edits. A theme declaring what it can do keeps the condition in one place: the
/// interface asks whether the active theme has a capability, never which theme it is.
///
/// Flags rather than a single value because these are independent. A theme could reasonably paint
/// a gradient without offering transparency, and every flat opaque theme declares neither.
/// </remarks>
[Flags]
public enum ThemeCapabilities
{
    /// <summary>A flat, opaque theme. What all five of the original themes are.</summary>
    None = 0,

    /// <summary>
    /// The theme's surface gradient is worth seeing.
    /// </summary>
    /// <remarks>
    /// Every theme supplies gradient stops and the gradient brush is painted for all of them, so
    /// this is not what makes the gradient work. A flat theme simply sets its three stops to the
    /// same colour, which is one code path rather than a branch. This flag says the difference is
    /// deliberate and visible, which is what a preview needs to know.
    /// </remarks>
    GradientSurface = 1,

    /// <summary>
    /// The window base layer may be made translucent, so what is behind the application shows
    /// through it.
    /// </summary>
    WindowTransparency = 2
}
