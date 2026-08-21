namespace LocalNEXUS.App.Nodes;

/// <summary>
/// What a compile check does to the run when the code will not compile.
/// </summary>
/// <remarks>
/// Faulting is the default, because the point of the check is that a run reporting success should
/// mean the code compiles. Passing it on with a warning is for the case where the graph does
/// something useful with broken code, for example writing it somewhere to look at by hand.
/// </remarks>
public enum CompileFailureBehaviour
{
    /// <summary>
    /// The file is marked as not compiling and the run carries on to the rest of the plan.
    /// </summary>
    /// <remarks>
    /// The default, and what makes a run recoverable. Stopping at the third file of eight throws
    /// away the four that would have worked and every step that had not run yet, and with a local
    /// model one file out of eight failing is an ordinary afternoon rather than an emergency. The
    /// retry limit is still spent on the file before it is given up on; what changes is that
    /// giving up on one file is not giving up on the run.
    /// </remarks>
    StageForLater,

    /// <summary>The run stops and reports the remaining errors.</summary>
    FaultTheRun,

    /// <summary>The run continues, the code is passed on, and the feed says it does not compile.</summary>
    ContinueWithWarning
}
