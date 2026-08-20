namespace LocalNEXUS.App.Services.Planning;

/// <summary>One existing file, and what the planner decided to do about it.</summary>
/// <param name="RelativePath">The file, relative to the project root.</param>
/// <param name="Decision">Use it, edit it, or build something new that references it.</param>
/// <param name="Symbol">The type the new work must tie into, when the decision names one.</param>
/// <param name="Reason">Why, in the planner's own words, for the activity feed.</param>
public sealed record CandidateVerdict(
    string RelativePath,
    CandidateDecision Decision,
    string? Symbol,
    string Reason)
{
    /// <summary>The verdict as one line, which is exactly what the feed shows.</summary>
    public override string ToString()
    {
        var decision = Decision switch
        {
            CandidateDecision.UseAsIs => "use as is",
            CandidateDecision.Edit => "edit",
            CandidateDecision.CreateNewReferencing => $"create new referencing {Symbol}",
            CandidateDecision.Ignore => "not relevant",
            _ => "undecided"
        };

        return Reason.Length == 0
            ? $"{RelativePath}: {decision}"
            : $"{RelativePath}: {decision}, {Reason}";
    }
}
