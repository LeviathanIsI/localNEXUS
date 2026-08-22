namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// What kind of decision a run is recording.
/// </summary>
/// <remarks>
/// Every one of these is a judgement the application made on somebody's behalf and then had
/// nowhere to put. They were written to the feed as sentences, which is right for reading and
/// useless for anything that needs to know what happened rather than read about it.
/// </remarks>
public enum RunDecisionKind
{
    /// <summary>What triage decided to do about a file the project already has.</summary>
    CandidateVerdict,

    /// <summary>A type the duplicate guard refused to let a plan create a second copy of.</summary>
    DuplicateRefused,

    /// <summary>A write a project rule refused, and which rule refused it.</summary>
    WriteRefused
}

/// <summary>
/// One decision a run made, in a shape something other than a person can read.
/// </summary>
/// <remarks>
/// The two findings this application exists to produce, that it refused to create a second copy of
/// something and that it refused a write which would have broken a scene, were both reported only
/// as prose in the activity feed. Nothing durable said they happened, so nothing could count them,
/// and a measurement of either came out as zero whether or not anything had occurred.
///
/// Deliberately node agnostic. The executor knows nothing about node types and that does not
/// change here: this is a general record with a kind and a rule name, so a node type added later
/// records into it without anything in the run knowing what that node is. <c>Rule</c> is a string
/// rather than an enum for the same reason, and the strings that go into it come from enums at the
/// places that raise them, so they are exact rather than invented at the call site.
/// </remarks>
/// <param name="Kind">Which sort of decision this is.</param>
/// <param name="Rule">Which rule or verdict, named exactly. Empty when the kind is the whole story.</param>
/// <param name="RelativePath">The file it was about, relative to the project root.</param>
/// <param name="Subject">The type involved, when there is one.</param>
/// <param name="OtherPath">Where the thing it collided with already lives, when there is one.</param>
/// <param name="Detail">The sentence a person would read.</param>
public sealed record RunDecision(
    RunDecisionKind Kind,
    string Rule,
    string RelativePath,
    string? Subject,
    string? OtherPath,
    string Detail)
{
    public override string ToString()
        => Subject is { Length: > 0 }
            ? $"{Kind} {Rule} on {RelativePath} ({Subject})"
            : $"{Kind} {Rule} on {RelativePath}";
}
