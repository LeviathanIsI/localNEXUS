namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// The computed answer to "can this model run right now": the section chain, who holds each
/// section, how much slack each section has, and a single gate the model node checks before
/// it sends anything. A gap in any section means there is no valid pipeline.
/// </summary>
/// <remarks>
/// The plan is now a reading of the mesh's own topology rather than something this install
/// computes. The gate stays, because a node still has to refuse a model the network cannot
/// currently assemble, and it should say which section is missing when it does.
/// </remarks>
public sealed class CoveragePlan
{
    public CoveragePlan(IReadOnlyList<SourceAssignment> assignments)
    {
        if (assignments.Count == 0)
        {
            throw new ArgumentException("A coverage plan needs at least one section.", nameof(assignments));
        }

        Assignments = assignments;
        WeakestAssignment = assignments.MinBy(a => a.Depth3 ? 3 : a.Depth2 ? 2 : a.Depth1 ? 1 : 0)!;

        var uncovered = assignments.FirstOrDefault(a => !a.IsCovered);
        IsComplete = uncovered is null;
        IncompleteReason = uncovered switch
        {
            null => null,
            { Source: null } => $"No source holds {uncovered.Section.Label}.",
            _ => $"{uncovered.Section.Label} is on {uncovered.SourceText} but not serving ({uncovered.StateText})."
        };
    }

    /// <summary>One entry per section, in pipeline order.</summary>
    public IReadOnlyList<SourceAssignment> Assignments { get; }

    /// <summary>The assignment with the least slack: the weakest link in the chain.</summary>
    public SourceAssignment WeakestAssignment { get; }

    /// <summary>The single gate. False means the run must be refused, with <see cref="IncompleteReason"/> as the message.</summary>
    public bool IsComplete { get; }

    /// <summary>Why the plan is incomplete, naming the section at fault. Null when complete.</summary>
    public string? IncompleteReason { get; }

    /// <summary>True when the pipeline spans more than one section, which means the mesh split it.</summary>
    public bool IsSplit => Assignments.Count > 1;

    /// <summary>How many distinct sources hold pieces of this model.</summary>
    public int SourceCount => Assignments
        .Select(a => a.Source)
        .OfType<InferenceSource>()
        .Select(s => s.SourceId)
        .Distinct()
        .Count();

    /// <summary>Slack behind the weakest section, which is what the model's strength bars show.</summary>
    public int WeakestSpare => Assignments.Min(a => a.SpareSources);

    /// <summary>One line for status messages: who holds what.</summary>
    public string Summary => string.Join(", ", Assignments.Select(a =>
        $"{a.Section.Label}: {a.SourceText}"));
}
