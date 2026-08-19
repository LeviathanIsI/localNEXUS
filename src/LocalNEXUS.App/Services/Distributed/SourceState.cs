namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// The observed condition of an inference source, as the mesh reports it.
/// </summary>
/// <remarks>
/// This install no longer probes anything itself: membership, liveness and role are the
/// engine's to determine and ours to render. A source that stops answering the mesh's own
/// heartbeat is dropped from the peer list rather than sitting here as unreachable, so
/// <see cref="Unreachable"/> covers the short window between a peer going quiet and the mesh
/// retiring it.
/// </remarks>
public enum SourceState
{
    /// <summary>Present in the mesh but its role has not been reported yet.</summary>
    Unknown,

    /// <summary>Joined and routing, but serving no model of its own.</summary>
    Available,

    /// <summary>Serving at least one model or holding a stage of one.</summary>
    Serving,

    /// <summary>Known to the mesh but not currently answering it.</summary>
    Unreachable
}
