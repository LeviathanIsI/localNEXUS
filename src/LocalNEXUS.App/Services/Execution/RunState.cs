namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// The state of a graph run. Every command in the UI decides whether it is available by
/// reading this enum, rather than by tracking separate flags that can disagree.
/// </summary>
public enum RunState
{
    /// <summary>No run has started, or the previous one has been cleared.</summary>
    Idle,

    /// <summary>Nodes are executing.</summary>
    Running,

    /// <summary>The run is holding between nodes at the user's request.</summary>
    Paused,

    /// <summary>Every node finished successfully.</summary>
    Completed,

    /// <summary>A node threw, the graph could not be ordered, or the user stopped the run.</summary>
    Faulted
}
