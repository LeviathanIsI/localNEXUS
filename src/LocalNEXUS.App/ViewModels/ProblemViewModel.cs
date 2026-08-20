using LocalNEXUS.App.Services.Compilation;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// One row of the Problems panel: a compiler diagnostic, and which node reported it.
/// </summary>
/// <remarks>
/// A wrapper rather than the diagnostic itself, because the diagnostic is the compiler backend's
/// contract and has no business growing a display string. Which node found it is the other half:
/// a graph can hold more than one compile check, and a list that does not say which one spoke
/// leaves the person to guess.
/// </remarks>
public sealed class ProblemViewModel
{
    public ProblemViewModel(CompileDiagnostic diagnostic, string nodeTitle)
    {
        Diagnostic = diagnostic;
        NodeTitle = nodeTitle;
    }

    /// <summary>What the compiler said.</summary>
    public CompileDiagnostic Diagnostic { get; }

    /// <summary>The compile check node that reported it.</summary>
    public string NodeTitle { get; }

    /// <summary>Whether this is what stopped the compile.</summary>
    public CompileSeverity Severity => Diagnostic.Severity;

    /// <summary>The compiler code, for example CS0103.</summary>
    public string Id => Diagnostic.Id;

    /// <summary>The file the diagnostic belongs to.</summary>
    public string File => Diagnostic.File;

    /// <summary>What the compiler said, in its own words.</summary>
    public string Message => Diagnostic.Message;

    /// <summary>Line and column, or blank when the diagnostic has no location.</summary>
    public string LocationText => Diagnostic.Line > 0
        ? $"{Diagnostic.Line},{Diagnostic.Column}"
        : string.Empty;

    /// <summary>The single character in front of the row, which is severity at a glance.</summary>
    public string SeverityGlyph => Diagnostic.Severity switch
    {
        CompileSeverity.Error => "x",
        CompileSeverity.Warning => "!",
        _ => "i"
    };
}
