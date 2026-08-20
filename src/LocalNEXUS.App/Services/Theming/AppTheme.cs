namespace LocalNEXUS.App.Services.Theming;

/// <summary>
/// The themes shipped with the application.
/// </summary>
/// <remarks>
/// The names are written to the configuration file, so they are part of its format. A value this
/// build does not recognise falls back to <see cref="EditorDark"/> rather than leaving the window
/// unpainted, and <see cref="Persistence.AppConfig"/> carries the one rename this enum has had.
/// </remarks>
public enum AppTheme
{
    /// <summary>The reference palette, and what the shell was designed against.</summary>
    EditorDark,

    /// <summary>Cool blue black, bright accent.</summary>
    DeepSlate,

    /// <summary>Softer contrast, for a long session.</summary>
    WarmCharcoal,

    /// <summary>Highest contrast of the dark themes, violet accent.</summary>
    NearBlack,

    /// <summary>Light, with every state colour chosen for a light background.</summary>
    Light
}
