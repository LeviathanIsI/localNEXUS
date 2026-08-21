namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// One thing the compiler had to say, located in the file it was said about.
/// </summary>
/// <remarks>
/// Deliberately not a Roslyn type. What a compiler backend is exposes nothing beyond this record,
/// so a different backend reporting the same facts is a drop in replacement, and a node reading
/// these never learns which compiler produced them.
/// </remarks>
/// <param name="Severity">Whether this stopped the compile.</param>
/// <param name="Id">The compiler code, for example CS0103.</param>
/// <param name="File">The file the diagnostic belongs to.</param>
/// <param name="Line">One based line number, or zero when the diagnostic has no location.</param>
/// <param name="Column">One based column number, or zero when the diagnostic has no location.</param>
/// <param name="Message">What the compiler said, in its own words.</param>
/// <param name="MayBeMissingReference">
/// True when a reference the check did not have could have caused this, rather than the code.
/// </param>
public sealed record CompileDiagnostic(
    CompileSeverity Severity,
    string Id,
    string File,
    int Line,
    int Column,
    string Message,
    bool MayBeMissingReference = false)
{
    /// <summary>
    /// The diagnostic codes that a missing reference produces, which are the same codes a genuine
    /// mistake produces.
    /// </summary>
    /// <remarks>
    /// This is the whole problem with a partial reference set. A project's own type, absent
    /// because the project has not been compiled, comes back as CS0246, exactly as a name the
    /// model invented does. Nothing in the diagnostic separates them, so the reference state has
    /// to, and these are the codes where that distinction applies.
    /// </remarks>
    private static readonly HashSet<string> ReferenceCodes = new(StringComparer.Ordinal)
    {
        "CS0012", // defined in an assembly that is not referenced
        "CS0103", // name does not exist in the current context
        "CS0117", // does not contain a definition
        "CS0234", // type or namespace does not exist in the namespace
        "CS0246", // type or namespace could not be found
        "CS1061"  // no definition and no accessible extension method
    };

    /// <summary>True when this code is one an absent reference can produce.</summary>
    public static bool IsReferenceCode(string id) => ReferenceCodes.Contains(id);

    /// <summary>True when this is what made the compile fail.</summary>
    public bool IsError => Severity == CompileSeverity.Error;

    /// <summary>
    /// True when this error is the code's own fault as far as anything here can tell.
    /// </summary>
    public bool IsTrustedError => IsError && !MayBeMissingReference;

    /// <summary>
    /// The diagnostic in the shape every C# compiler prints it, which is also the shape a model
    /// has seen ten thousand times in its training data.
    /// </summary>
    public override string ToString()
    {
        var severity = Severity switch
        {
            CompileSeverity.Error => "error",
            CompileSeverity.Warning => "warning",
            _ => "info"
        };

        var note = MayBeMissingReference
            ? "  [may be a reference this check did not have, rather than a mistake]"
            : string.Empty;

        return Line > 0
            ? $"{File}({Line},{Column}): {severity} {Id}: {Message}{note}"
            : $"{File}: {severity} {Id}: {Message}{note}";
    }
}
