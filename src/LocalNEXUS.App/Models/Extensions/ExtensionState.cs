namespace LocalNEXUS.App.Models.Extensions;

/// <summary>
/// What is true of an extension right now.
/// </summary>
/// <remarks>
/// Six states rather than a pair of booleans, for the same reason the run has an explicit state
/// machine: the interesting cases are the ones in between. In particular
/// <see cref="NotInstalled"/> and <see cref="Failed"/> are not the same thing and must never be
/// drawn the same way. An extension that has never been started has not failed, and saying it
/// has is how a person is sent looking for a problem that does not exist.
/// </remarks>
public enum ExtensionState
{
    /// <summary>Known about, listed, and nothing has been put on disk.</summary>
    NotInstalled,

    /// <summary>Being fetched or unpacked right now.</summary>
    Installing,

    /// <summary>The process has been launched and has not yet answered.</summary>
    Starting,

    /// <summary>Started, answered, and usable.</summary>
    Running,

    /// <summary>Installed and configured, but the last attempt to reach it did not answer.</summary>
    Unreachable,

    /// <summary>Configured wrongly or broken. The reason is always recorded alongside.</summary>
    Failed
}
