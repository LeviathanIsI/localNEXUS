namespace LocalNEXUS.App.Services.Theming;

/// <summary>
/// The themes shipped with the application.
/// </summary>
/// <remarks>
/// The names are written to the configuration file, so they are part of its format and are not
/// renamed casually. A value that is no longer recognised falls back to <see cref="VsCodeDark"/>
/// rather than leaving the window unpainted.
/// </remarks>
public enum AppTheme
{
    /// <summary>The reference palette the shell was designed against.</summary>
    VsCodeDark,

    /// <summary>Cool and very dark, with a bright blue accent.</summary>
    DeepSlate,

    /// <summary>Softer contrast, which suits long sessions.</summary>
    WarmCharcoal,

    /// <summary>The highest contrast of the dark themes, with a violet accent.</summary>
    NearBlack,

    /// <summary>Light, with every state colour chosen for a light background rather than inverted.</summary>
    Light
}
