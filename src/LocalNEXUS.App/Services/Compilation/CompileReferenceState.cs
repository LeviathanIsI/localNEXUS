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

    /// <summary>
    /// The open project's own source and its restored packages were both read, so the check sees
    /// what a real build of that project would see.
    /// </summary>
    /// <remarks>
    /// The non Unity counterpart of <see cref="Complete"/>, and the point of it is that a missing
    /// type under this state means the code is wrong rather than that the check was short of
    /// something. What it still does not have is whatever the project's source generators and
    /// analyzers would contribute, which is the same thing the Unity path gives up.
    /// </remarks>
    ProjectResolved,

    /// <summary>
    /// The project's own source was read, but nothing has restored it, so its packages are absent.
    /// </summary>
    /// <remarks>
    /// An ordinary state rather than a failure: a project nobody has built yet has no restore
    /// record, and there is nothing wrong with that. Types the project declares resolve; a type
    /// from a package does not, so a missing type is still not trusted here.
    /// </remarks>
    ProjectNotRestored,

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
