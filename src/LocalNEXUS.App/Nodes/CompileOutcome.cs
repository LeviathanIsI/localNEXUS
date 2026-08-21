namespace LocalNEXUS.App.Nodes;

/// <summary>
/// How a compile check ended.
/// </summary>
/// <remarks>
/// Separate from the node's execution state because they answer different questions. A check that
/// completed having repaired the code three times is a completed node with a result worth saying
/// out loud, and a check that could not run at all is not a check that failed.
///
/// <see cref="NotRun"/> is first so an unset value reads as a question rather than a verdict, and
/// <see cref="Checking"/> exists so that work in progress never renders as a failure.
/// </remarks>
public enum CompileOutcome
{
    /// <summary>No check has run yet.</summary>
    NotRun,

    /// <summary>A check is in progress. Not a failure.</summary>
    Checking,

    /// <summary>The code compiled on the first attempt.</summary>
    Compiled,

    /// <summary>The code failed, was repaired, and then compiled.</summary>
    Repaired,

    /// <summary>The code did not compile and could not be repaired within the attempt limit.</summary>
    Failed,

    /// <summary>
    /// The check ran, complained, and every complaint could be a reference it did not have.
    /// </summary>
    /// <remarks>
    /// Not a failure and not a pass. Under a partial reference set a type the project defines and
    /// a type the model invented come back as the same diagnostic, so a result made entirely of
    /// those has not told anyone anything about the code. No repair is spent on it and the run
    /// carries on.
    /// </remarks>
    Inconclusive,

    /// <summary>
    /// Nothing was wrong with the code as far as anyone knows, because the check could not be
    /// run. No Unity install, or no project open.
    /// </summary>
    Unavailable
}
