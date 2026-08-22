namespace LocalNEXUS.App.Services.Spec;

/// <summary>
/// Where an artifact of a change has got to.
/// </summary>
/// <remarks>
/// Three states and not two, following the discipline set in v0.6. Blocked is not failed: an
/// artifact that cannot be written yet because the one it depends on has not been written is in a
/// perfectly ordinary state, and drawing it as a failure would blame it for waiting its turn.
///
/// OpenSpec decides which of these an artifact is. Nothing here works it out, because a second
/// implementation of its dependency resolution would drift from it and then be wrong in a way
/// nobody would notice until it mattered.
/// </remarks>
public enum SpecArtifactState
{
    /// <summary>The worker reported something this build does not recognise.</summary>
    Unknown,

    /// <summary>Written and accepted.</summary>
    Done,

    /// <summary>Everything it depends on is done, so this is the next thing that can be written.</summary>
    Ready,

    /// <summary>Waiting on something else. Not a failure.</summary>
    Blocked
}

/// <summary>Whether a change is still being worked on or has been folded back into the specs.</summary>
public enum SpecChangeStatus
{
    /// <summary>Being worked on.</summary>
    Active,

    /// <summary>Completed and archived.</summary>
    Archived
}

/// <summary>
/// One artifact of a change: a proposal, a spec delta, a design or a task list.
/// </summary>
/// <param name="Id">What the worker calls it, which is what is sent back to read or advance it.</param>
/// <param name="Name">What to show.</param>
/// <param name="State">Done, ready or blocked, as OpenSpec reports it.</param>
/// <param name="Detail">Why it is in that state, when the worker says.</param>
public sealed record SpecArtifact(string Id, string Name, SpecArtifactState State, string? Detail);

/// <summary>
/// One change, and the artifacts it is made of.
/// </summary>
/// <param name="Id">What the worker calls it.</param>
/// <param name="Name">What to show.</param>
/// <param name="Status">Active or archived.</param>
/// <param name="Artifacts">Its artifacts, in the order the worker gave them.</param>
public sealed record SpecChange(
    string Id,
    string Name,
    SpecChangeStatus Status,
    IReadOnlyList<SpecArtifact> Artifacts)
{
    /// <summary>The next artifact that could be written, or null when there is none.</summary>
    /// <remarks>
    /// Read off what the worker reported rather than worked out here. Which artifact is next is
    /// exactly the question OpenSpec's artifact graph answers, and answering it a second time is
    /// how the two come to disagree.
    /// </remarks>
    public SpecArtifact? NextReady => Artifacts.FirstOrDefault(a => a.State == SpecArtifactState.Ready);

    /// <summary>One line for the list.</summary>
    public string Summary
    {
        get
        {
            var done = Artifacts.Count(a => a.State == SpecArtifactState.Done);

            return $"{done} of {Artifacts.Count} done"
                   + (NextReady is { } next ? $", next is {next.Name}" : string.Empty);
        }
    }
}

/// <summary>What an artifact holds, and where it came from.</summary>
/// <param name="Content">The text itself.</param>
/// <param name="Path">Where it lives, for somebody who wants to open it properly.</param>
public sealed record SpecArtifactContent(string Content, string? Path);

/// <summary>What the worker said about itself when it started.</summary>
/// <param name="Tool">What it is bridging to, which is OpenSpec.</param>
/// <param name="Version">Which version of it.</param>
/// <param name="Root">The folder its changes and specs live in.</param>
public sealed record SpecWorkerInfo(string Tool, string Version, string? Root);
