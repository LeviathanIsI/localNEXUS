namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// A node state as it is drawn, which has one value more than the execution state does.
/// </summary>
/// <remarks>
/// The extra value is <see cref="Skipped"/>, and it exists because a fault has to stay on the
/// node that faulted. When a run stops, every node after the one that failed is still Pending,
/// and there are two very different reasons a node can be Pending: it is waiting its turn in a
/// run that is still going, or the run ended before it was reached. The first is worth showing
/// as an ordinary queue; the second is worth saying out loud, because the interesting fact about
/// that node is that it never ran, and painting it red would blame it for someone else's failure.
///
/// This is presentation only. Nothing in the execution model has a skipped state, and the
/// executor is not told about this one.
/// </remarks>
public enum NodeDisplayState
{
    /// <summary>Has not run yet in a run that has not finished. Quiet, not a warning.</summary>
    Pending,

    /// <summary>Executing now.</summary>
    Running,

    /// <summary>Finished and produced its outputs.</summary>
    Completed,

    /// <summary>This node is the one that failed.</summary>
    Faulted,

    /// <summary>The run ended before this node was reached, so it never ran and never will.</summary>
    Skipped
}
