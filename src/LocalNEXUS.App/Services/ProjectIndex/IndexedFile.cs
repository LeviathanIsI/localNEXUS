namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>One source file in the project, as the index knows it.</summary>
/// <remarks>
/// The write time and length together are the cache key. Content hashing would be stricter and
/// would cost a full read of every file on every refresh, which is exactly what the cache exists
/// to avoid; a file whose length and timestamp are both unchanged has not been edited by anything
/// a person did.
/// </remarks>
public sealed class IndexedFile
{
    public IndexedFile(
        string relativePath,
        DateTime lastWriteUtc,
        long length,
        string @namespace,
        IReadOnlyList<IndexedType> types,
        IReadOnlyList<string> referencedTypeNames)
    {
        RelativePath = relativePath;
        LastWriteUtc = lastWriteUtc;
        Length = length;
        Namespace = @namespace;
        Types = types;
        ReferencedTypeNames = referencedTypeNames;
    }

    /// <summary>Path relative to the project root, using forward slashes, as Unity writes them.</summary>
    public string RelativePath { get; }

    /// <summary>When the file was last written, in UTC.</summary>
    public DateTime LastWriteUtc { get; }

    /// <summary>Length in bytes.</summary>
    public long Length { get; }

    /// <summary>The first namespace declared in the file, or an empty string.</summary>
    public string Namespace { get; }

    /// <summary>Every type the file declares.</summary>
    public IReadOnlyList<IndexedType> Types { get; }

    /// <summary>
    /// Type names the file mentions but does not declare. Syntactic, so it includes some things
    /// that are not types at all; that is acceptable for a reference graph whose only job is to
    /// say which files are near each other.
    /// </summary>
    public IReadOnlyList<string> ReferencedTypeNames { get; }

    /// <summary>The file name without its folder, which is what Unity ties to a class name.</summary>
    public string FileName => System.IO.Path.GetFileName(RelativePath);

    /// <summary>True when this file sits under an Editor folder, so it cannot ship in a build.</summary>
    public bool IsEditorOnly
        => RelativePath.Contains("/Editor/", StringComparison.OrdinalIgnoreCase)
           || RelativePath.EndsWith("/Editor", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => RelativePath;
}
