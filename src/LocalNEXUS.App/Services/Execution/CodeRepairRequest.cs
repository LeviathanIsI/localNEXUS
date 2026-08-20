using LocalNEXUS.App.Services.Compilation;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// What a node is told when it is asked to fix code it produced.
/// </summary>
/// <remarks>
/// Carries what a person would need to do the job and nothing else: what was asked for, what came
/// back, and what the compiler objected to. The failing code is sent whole because a fix has to
/// be a whole file, and the diagnostics are capped because a single missing brace can produce
/// fifty knock on errors and burying the real one under them makes the fix less likely, not more.
/// </remarks>
/// <param name="Attempt">Which repair attempt this is, counting from one.</param>
/// <param name="AttemptLimit">How many repair attempts are allowed in total.</param>
/// <param name="FileName">The name the code will be written under, which Unity ties to its type name.</param>
/// <param name="FailingCode">The code exactly as it failed.</param>
/// <param name="Diagnostics">What the compiler said, errors first and already capped.</param>
public sealed record CodeRepairRequest(
    int Attempt,
    int AttemptLimit,
    string FileName,
    string FailingCode,
    IReadOnlyList<CompileDiagnostic> Diagnostics)
{
    /// <summary>The diagnostics as a compiler prints them, one per line.</summary>
    public string FormattedDiagnostics
        => string.Join(Environment.NewLine, Diagnostics.Select(d => d.ToString()));

    /// <summary>How many of the diagnostics stopped the compile.</summary>
    public int ErrorCount => Diagnostics.Count(d => d.IsError);
}
