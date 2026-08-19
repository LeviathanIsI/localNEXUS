namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// The condition of this install's mesh node, which is the process the distributed path runs on.
/// </summary>
/// <remarks>
/// This is the state of our own node, not of a peer. Peer condition lives on
/// <see cref="InferenceSource.State"/> and comes from what the mesh reports about them.
/// </remarks>
public enum MeshNodeState
{
    /// <summary>No node process is running. The distributed path is unavailable.</summary>
    Stopped,

    /// <summary>The process has been spawned and has not answered its management API yet.</summary>
    Starting,

    /// <summary>Running and routing requests, but serving nothing of its own.</summary>
    Client,

    /// <summary>Running and offering this machine's compute to the mesh.</summary>
    Serving,

    /// <summary>The process exited or never became answerable. The reason is on the manager.</summary>
    Failed
}
