namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// The computed answer to "can this model run right now": the section chain, who fills each
/// section, how much slack each section has, and a single gate the launcher checks before
/// anything is spawned. A gap in any section means there is no valid pipeline.
/// </summary>
public sealed class CoveragePlan
{
    public CoveragePlan(IReadOnlyList<SourceAssignment> assignments)
    {
        if (assignments.Count == 0)
        {
            throw new ArgumentException("A coverage plan needs at least one section.", nameof(assignments));
        }

        Assignments = assignments;
        WeakestAssignment = assignments.MinBy(a => a.Redundancy)!;

        var uncovered = assignments.FirstOrDefault(a => !a.IsCovered);
        IsComplete = uncovered is null;
        IncompleteReason = uncovered is null
            ? null
            : $"No source covers {uncovered.Section.Label}. Add a source with enough memory or free this machine's.";
    }

    /// <summary>One entry per section, in pipeline order.</summary>
    public IReadOnlyList<SourceAssignment> Assignments { get; }

    /// <summary>The assignment with the least redundancy: the weakest link in the chain.</summary>
    public SourceAssignment WeakestAssignment { get; }

    /// <summary>The single gate. False means the run must be refused, with <see cref="IncompleteReason"/> as the message.</summary>
    public bool IsComplete { get; }

    /// <summary>Why the plan is incomplete, naming the uncovered section. Null when complete.</summary>
    public string? IncompleteReason { get; }

    /// <summary>True when the plan spans more than one source, which means an rpc launch.</summary>
    public bool IsSplit => Assignments.Count > 1;

    /// <summary>
    /// The remote endpoints in the order llama-server should be given them. RPC devices are
    /// registered ahead of local devices by llama.cpp, and the planner builds sections in the
    /// same order, so this list lines up with the tensor split below.
    /// </summary>
    public IReadOnlyList<string> RpcEndpoints => Assignments
        .Where(a => a.Source is { IsThisMachine: false })
        .Select(a => a.Source!.EndpointText)
        .ToList();

    /// <summary>Tensor split proportions in section order, matching llama.cpp device order.</summary>
    public IReadOnlyList<double> TensorSplit => Assignments.Select(a => a.Proportion).ToList();

    /// <summary>One line for status messages: who serves what.</summary>
    public string Summary => string.Join(", ", Assignments.Select(a =>
        $"{a.Section.Label}: {a.SourceText}"));
}
