namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// The computed answer to "can this model run right now": the section chain, who holds each
/// section, how much slack each section has, and a single verdict the model node checks before
/// it sends anything.
/// </summary>
/// <remarks>
/// The plan is a reading of the mesh's own topology rather than something this install
/// computes. The verdict stays, because a node still has to refuse a model the network cannot
/// currently assemble and should say which section is at fault when it does.
///
/// The verdict is three way. A section the mesh has not finished bringing up leaves the plan
/// starting, not blocked, and a genuine gap beats a section still loading: knowing one section
/// cannot serve settles the question whatever the rest are doing.
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
        WeakestAssignment = assignments.MinBy(Strength)!;

        if (assignments.FirstOrDefault(a => a.IsBlocking) is { } blocking)
        {
            Availability = ModelAvailability.Blocked;
            StatusDetail = blocking.StatusDetail;
        }
        else if (assignments.FirstOrDefault(a => !a.IsCovered) is { } arriving)
        {
            Availability = ModelAvailability.Starting;
            StatusDetail = arriving.StatusDetail;
        }
        else
        {
            Availability = ModelAvailability.Complete;
        }
    }

    /// <summary>One entry per section, in pipeline order.</summary>
    public IReadOnlyList<SourceAssignment> Assignments { get; }

    /// <summary>The assignment with the least slack: the weakest link in the chain.</summary>
    public SourceAssignment WeakestAssignment { get; }

    /// <summary>The verdict. Anything but <see cref="ModelAvailability.Complete"/> means the run must be refused.</summary>
    public ModelAvailability Availability { get; }

    /// <summary>What the section at fault, or the section still arriving, is doing. Null when complete.</summary>
    public string? StatusDetail { get; }

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

    /// <summary>Orders sections from the least to the most slack, so the weakest link sorts first.</summary>
    private static int Strength(SourceAssignment assignment)
        => assignment.Depth3 ? 3 : assignment.Depth2 ? 2 : assignment.Depth1 ? 1 : 0;
}
