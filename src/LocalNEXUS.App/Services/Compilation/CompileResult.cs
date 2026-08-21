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

    /// <summary>The errors that a reference this check did not have cannot explain away.</summary>
    public IReadOnlyList<CompileDiagnostic> TrustedErrors
        => Diagnostics.Where(d => d.IsTrustedError).ToList();

    /// <summary>
    /// True when the compile failed and every reason it gave could be a reference it did not have.
    /// </summary>
    /// <remarks>
    /// The state that keeps the v1.0 rule honest under a partial reference set. Code that cannot
    /// be checked is not code that is broken, and a check whose every complaint is a name it was
    /// never given the means to resolve has not checked the code. Reporting that as a failure
    /// spends the repair limit asking a model to fix something that is not wrong, and then tells
    /// the user their code is broken when the truth is that the project has not been compiled.
    ///
    /// One error that references cannot explain is enough to make the result a real one. A missing
    /// brace is a missing brace whatever the set contained.
    /// </remarks>
    public bool IsInconclusive => !Succeeded && Errors.Count > 0 && TrustedErrors.Count == 0;

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
