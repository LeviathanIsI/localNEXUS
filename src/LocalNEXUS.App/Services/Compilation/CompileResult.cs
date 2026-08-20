namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// What one compile attempt found.
/// </summary>
/// <remarks>
/// <see cref="Succeeded"/> answers only the question the check asks. Whether the references were
/// complete is a separate axis on purpose: a compile that passed against a partial reference set
/// is a weaker claim than one that passed against a complete one, and the panel says which.
/// </remarks>
public sealed class CompileResult
{
    public CompileResult(
        bool succeeded,
        IReadOnlyList<CompileDiagnostic> diagnostics,
        TimeSpan elapsed,
        CompileReferenceState referenceState,
        string referenceSummary)
    {
        Succeeded = succeeded;
        Diagnostics = diagnostics;
        Elapsed = elapsed;
        ReferenceState = referenceState;
        ReferenceSummary = referenceSummary;
    }

    /// <summary>True when nothing of error severity came back.</summary>
    public bool Succeeded { get; }

    /// <summary>Everything the compiler said, errors first.</summary>
    public IReadOnlyList<CompileDiagnostic> Diagnostics { get; }

    /// <summary>How long the attempt took.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>What could be found to compile against.</summary>
    public CompileReferenceState ReferenceState { get; }

    /// <summary>A sentence naming what was compiled against, so the claim can be judged.</summary>
    public string ReferenceSummary { get; }

    /// <summary>The diagnostics that stopped the compile.</summary>
    public IReadOnlyList<CompileDiagnostic> Errors
        => Diagnostics.Where(d => d.IsError).ToList();

    /// <summary>One line for the node footer.</summary>
    public string Summary
    {
        get
        {
            var errors = Errors.Count;
            var seconds = Elapsed.TotalMilliseconds;

            return errors == 0
                ? $"Compiled in {seconds:0} ms"
                : $"{errors} error(s) in {seconds:0} ms";
        }
    }

    /// <summary>
    /// The diagnostics as a compiler would print them, capped so that a file with a hundred
    /// knock on errors does not bury the first one that actually matters.
    /// </summary>
    public string FormatDiagnostics(int limit)
    {
        var ordered = Diagnostics
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.Line)
            .ToList();

        var shown = ordered.Take(limit).Select(d => d.ToString());
        var text = string.Join(Environment.NewLine, shown);

        return ordered.Count > limit
            ? $"{text}{Environment.NewLine}... and {ordered.Count - limit} more"
            : text;
    }
}
