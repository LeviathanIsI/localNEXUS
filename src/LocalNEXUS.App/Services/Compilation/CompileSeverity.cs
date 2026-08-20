namespace LocalNEXUS.App.Services.Compilation;

/// <summary>How much a compiler diagnostic matters.</summary>
/// <remarks>
/// Only <see cref="Error"/> decides whether a check passed. Warnings are carried because a
/// warning is often the clue that explains an error two lines further down, and because a model
/// asked to fix code does better when it can see them.
/// </remarks>
public enum CompileSeverity
{
    /// <summary>Advisory. Does not stop a compile.</summary>
    Info,

    /// <summary>A problem the compiler tolerated. Does not stop a compile.</summary>
    Warning,

    /// <summary>A problem that stopped the compile.</summary>
    Error
}
