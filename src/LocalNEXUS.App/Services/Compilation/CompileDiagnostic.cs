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
public sealed record CompileDiagnostic(
    CompileSeverity Severity,
    string Id,
    string File,
    int Line,
    int Column,
    string Message)
{
    /// <summary>True when this is what made the compile fail.</summary>
    public bool IsError => Severity == CompileSeverity.Error;

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

        return Line > 0
            ? $"{File}({Line},{Column}): {severity} {Id}: {Message}"
            : $"{File}: {severity} {Id}: {Message}";
    }
}
