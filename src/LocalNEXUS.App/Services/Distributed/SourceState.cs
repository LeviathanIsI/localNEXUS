namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// The observed condition of an inference source, driven by the health monitor.
/// </summary>
public enum SourceState
{
    /// <summary>Never probed. The state every remote source starts in.</summary>
    Unknown,

    /// <summary>A probe is in flight right now.</summary>
    Probing,

    /// <summary>The last probe reached the source.</summary>
    Available,

    /// <summary>The source is reachable but currently serving a pipeline.</summary>
    Busy,

    /// <summary>The last probe could not reach the source.</summary>
    Unreachable
}
