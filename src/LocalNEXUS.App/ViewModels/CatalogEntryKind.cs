namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The two ways a model gets into the catalogue.
/// </summary>
/// <remarks>
/// Worth keeping apart in the settings list even though the models themselves are
/// indistinguishable once found, because removing one is a different act: dropping a folder stops
/// a standing search, dropping a model stops offering that one thing.
/// </remarks>
public enum CatalogEntryKind
{
    /// <summary>A folder that is searched, and keeps being searched.</summary>
    ScannedFolder,

    /// <summary>One model, added by name.</summary>
    Model
}
