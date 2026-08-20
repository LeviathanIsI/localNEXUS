namespace LocalNEXUS.App.Models;

/// <summary>
/// The execution state of a single node within the current run. Every visual and
/// behavioural decision about a node reads this enum rather than a set of booleans.
/// </summary>
public enum NodeState
{
    /// <summary>Not executed yet during the current run.</summary>
    Pending,

    /// <summary>Currently executing.</summary>
    Running,

    /// <summary>Finished successfully and produced its outputs.</summary>
    Completed,

    /// <summary>Threw or was cancelled before producing outputs.</summary>
    Faulted
}
