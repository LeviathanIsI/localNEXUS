namespace LocalNEXUS.App.Services.Planning;

/// <summary>
/// What is to be done about an existing file that looked relevant.
/// </summary>
/// <remarks>
/// The three answers are deliberately exhaustive and deliberately explicit. Left implicit, the
/// shortest path for a model is always to write a new file, which is how a project ends up with
/// a second health component that nothing references.
/// </remarks>
public enum CandidateDecision
{
    /// <summary>Not looked at yet. The default so an unset value is never mistaken for a verdict.</summary>
    Undecided,

    /// <summary>The file already does the job. Reference it, change nothing.</summary>
    UseAsIs,

    /// <summary>The file is what the request is about. Change it.</summary>
    Edit,

    /// <summary>Something new is needed, and it has to tie into this file rather than ignore it.</summary>
    CreateNewReferencing,

    /// <summary>Looked at and found not to be relevant after all.</summary>
    Ignore
}
