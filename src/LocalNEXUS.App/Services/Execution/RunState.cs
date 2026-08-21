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

    /// <summary>Every node finished successfully and nothing was left outstanding.</summary>
    Completed,

    /// <summary>
    /// Every node finished, and the run left work behind that somebody has to decide about.
    /// </summary>
    /// <remarks>
    /// The state that neither of the other two described. A run that wrote four files and could
    /// not finish the fifth has not failed, because four files are on disk and correct, and it has
    /// not completed, because one is still waiting. Calling it either would be a lie in a place
    /// where somebody makes a decision from one word.
    /// </remarks>
    Unresolved,

    /// <summary>A node threw, the graph could not be ordered, or the user stopped the run.</summary>
    Faulted
}
