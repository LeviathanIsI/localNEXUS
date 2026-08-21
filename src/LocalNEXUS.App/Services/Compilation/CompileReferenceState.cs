namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// What could be found to compile against.
/// </summary>
/// <remarks>
/// Following the v0.6 discipline: not knowing is its own state and never renders as a failure.
/// A check that could not run is not a check that failed, and the difference matters, because
/// telling someone their code is broken when the truth is that no Unity install could be found
/// sends them looking in the wrong place.
/// </remarks>
public enum CompileReferenceState
{
    /// <summary>Nothing has looked yet.</summary>
    Unknown,

    /// <summary>The Unity install and the project's own compiled assemblies were both found.</summary>
    Complete,

    /// <summary>
    /// The Unity install was found but the project has no compiled assemblies yet, so the
    /// project's own types will not resolve. Unity API and language errors are still caught.
    /// </summary>
    ProjectNotCompiled,

    /// <summary>
    /// No Unity was involved at all, so the check ran against the framework alone.
    /// </summary>
    /// <remarks>
    /// The floor rather than a failure. Syntax and standard library mistakes are caught, and any
    /// type the surrounding project defines is unknown, which is why an error blaming a missing
    /// type is not trusted under this state.
    /// </remarks>
    FrameworkOnly,

    /// <summary>No Unity project is open, so there is nothing to compile against.</summary>
    NoProject,

    /// <summary>No Unity installation could be found on this machine.</summary>
    NoUnityInstall,

    /// <summary>
    /// Not even this build's own framework assemblies could be reached, so no compile of any kind
    /// is possible. The last resort has a resort of its own only because a single file build can
    /// legitimately be in this position.
    /// </summary>
    NoFrameworkReferences
}
