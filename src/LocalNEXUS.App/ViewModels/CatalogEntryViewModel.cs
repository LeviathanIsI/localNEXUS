namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// One row of the catalogue list in settings: a folder being scanned, or a model added by name.
/// </summary>
/// <param name="Path">The folder or model this row is about.</param>
/// <param name="Kind">Which of the two it is.</param>
/// <param name="Label">What the row leads with.</param>
/// <param name="Detail">The line under it: where the entry came from, or what the model is.</param>
/// <param name="CanRemove">True when the settings panel owns this entry and can drop it.</param>
public sealed record CatalogEntryViewModel(
    string Path,
    CatalogEntryKind Kind,
    string Label,
    string Detail,
    bool CanRemove)
{
    /// <summary>True for a folder, which is what decides the icon and the wording.</summary>
    public bool IsFolder => Kind == CatalogEntryKind.ScannedFolder;
}
