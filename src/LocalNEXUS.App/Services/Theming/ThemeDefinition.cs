namespace LocalNEXUS.App.Services.Theming;

/// <summary>
/// One shipped theme: what to call it, where its colours live, and a sentence about what it is
/// for.
/// </summary>
/// <param name="Theme">The value written to the configuration file.</param>
/// <param name="DisplayName">What the picker calls it.</param>
/// <param name="Description">One line explaining when someone would pick it.</param>
/// <param name="Source">Pack path of the dictionary holding its colours.</param>
public sealed record ThemeDefinition(
    AppTheme Theme,
    string DisplayName,
    string Description,
    string Source)
{
    /// <summary>The dictionary this theme lives in, as an absolute pack uri.</summary>
    public Uri Uri => new(Source, UriKind.RelativeOrAbsolute);
}
