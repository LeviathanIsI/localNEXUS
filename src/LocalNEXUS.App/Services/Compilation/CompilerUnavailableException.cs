namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// Thrown when a compile could not be attempted at all, as opposed to attempted and failed.
/// </summary>
/// <remarks>
/// Its own type because the two are not the same news and must not be reported as though they
/// were. Code that cannot be checked is not code that is broken.
/// </remarks>
public sealed class CompilerUnavailableException : Exception
{
    public CompilerUnavailableException(CompileReferenceState state, string message)
        : base(message)
        => State = state;

    /// <summary>What was missing.</summary>
    public CompileReferenceState State { get; }
}
