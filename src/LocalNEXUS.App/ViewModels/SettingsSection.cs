namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The sections of the settings panel, in the order they are listed.
/// </summary>
public enum SettingsSection
{
    /// <summary>Theme, and the type it is rendered in.</summary>
    Appearance,

    /// <summary>Where models are looked for, and how cloud providers are reached.</summary>
    Models,

    /// <summary>The open Unity project and what is known about its contents.</summary>
    Unity,

    /// <summary>The Python environment and the mesh node.</summary>
    Runtime,

    /// <summary>The values a newly added node starts from.</summary>
    Behaviour
}
