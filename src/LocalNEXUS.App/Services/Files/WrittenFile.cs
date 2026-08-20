namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// One file a batch put on disk, and how much of it changed.
/// </summary>
/// <param name="Path">Absolute path written.</param>
/// <param name="Bytes">How many bytes were written.</param>
/// <param name="Change">Lines added and removed against what was there before.</param>
public sealed record WrittenFile(string Path, long Bytes, DiffStat Change);
