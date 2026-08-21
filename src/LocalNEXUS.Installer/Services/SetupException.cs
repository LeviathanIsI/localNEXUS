namespace LocalNEXUS.Installer.Services;

/// <summary>
/// Something the installer could not do, said the way the person in front of it needs to hear it.
/// </summary>
/// <remarks>
/// Every message thrown here names what failed and what to do about it. An installer that dies
/// with a generic error is worse than one that never offered to download anything, because the
/// person is now half installed and has nothing to act on.
/// </remarks>
public sealed class SetupException : Exception
{
    public SetupException(string message)
        : base(message)
    {
    }

    public SetupException(string message, Exception inner)
        : base(message, inner)
    {
    }

    /// <summary>True when trying the same thing again could reasonably work.</summary>
    public bool CanRetry { get; init; }
}
