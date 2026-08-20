namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>
/// Where the project index has got to.
/// </summary>
/// <remarks>
/// Following the v0.6 discipline. Indexing is not a failure state and must never be drawn as one,
/// and neither is a project that has not been indexed yet. Only <see cref="Unavailable"/> means
/// something went wrong, and even that is about the project rather than about the code in it.
/// </remarks>
public enum ProjectIndexState
{
    /// <summary>Nothing has looked yet.</summary>
    Unknown,

    /// <summary>Reading the project right now. Not a failure.</summary>
    Indexing,

    /// <summary>Indexed, and there is something in it.</summary>
    Ready,

    /// <summary>Indexed, and the project has no C# in it yet. A perfectly ordinary state.</summary>
    Empty,

    /// <summary>No project is open, or its Assets folder could not be read.</summary>
    Unavailable
}
