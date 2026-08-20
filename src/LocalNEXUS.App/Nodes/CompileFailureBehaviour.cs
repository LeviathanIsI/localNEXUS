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
    /// <summary>The run stops and reports the remaining errors.</summary>
    FaultTheRun,

    /// <summary>The run continues, the code is passed on, and the feed says it does not compile.</summary>
    ContinueWithWarning
}
