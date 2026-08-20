namespace LocalNEXUS.App.Services.Theming;

/// <summary>
/// One shipped theme: what to call it, where its colours live, and a sentence about what it is
/// for.
/// </summary>
/// <param name="Theme">The value written to the configuration file.</param>
/// <param name="DisplayName">What the picker calls it.</param>
/// <param name="Description">One line explaining when someone would pick it.</param>
/// <param name="Source">Path of the dictionary holding its colours, relative to the assembly.</param>
public sealed record ThemeDefinition(
    AppTheme Theme,
    string DisplayName,
    string Description,
    string Source)
{
    /// <summary>
    /// Where this theme's colours live.
    /// </summary>
    /// <remarks>
    /// Relative rather than an absolute pack uri, because it is used both to merge the dictionary
    /// and to load a private uncached copy of it, and the loader takes only the relative form.
    /// </remarks>
    public Uri Uri => new(Source, UriKind.Relative);
}
