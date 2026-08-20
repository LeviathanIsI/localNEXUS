namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// One folder the model catalogue scans, and whether the settings panel is allowed to stop
/// scanning it.
/// </summary>
/// <param name="Path">The folder.</param>
/// <param name="CanRemove">True when this folder was added here rather than being built in or listed in model-paths.txt.</param>
/// <param name="Origin">Where the folder came from, so a row that cannot be removed says why.</param>
public sealed record ModelFolderViewModel(string Path, bool CanRemove, string Origin);
