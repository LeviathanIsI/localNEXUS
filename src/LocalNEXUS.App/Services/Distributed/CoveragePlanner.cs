namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Computes coverage plans: which sources fill which sections of a model, and whether the
/// resulting pipeline is complete enough to launch at all.
/// </summary>
/// <remarks>
/// Distribution is a capability unlock, not a speedup, so the planner prefers running a model
/// entirely on this machine and splits only when the model does not fit or a split is forced
/// for testing. Sections are built in the order llama.cpp registers devices, RPC backends
/// first and the local GPU last, so tensor split proportions line up with reality.
/// </remarks>
public sealed class CoveragePlanner
{
    /// <summary>The smallest share of a model worth sending to any one source.</summary>
    private const double MinimumShare = 0.05d;

    private readonly SourceRegistry _registry;

    public CoveragePlanner(SourceRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Plans coverage for a model.
    /// </summary>
    /// <param name="model">The model's metadata, read from its GGUF header.</param>
    /// <param name="forceSplit">Splits even when the model fits locally. Needed to exercise distribution with a small model.</param>
    /// <param name="manualProportions">Overrides the automatic by memory proportions when its length matches the participant count.</param>
    public CoveragePlan Plan(GgufModelInfo model, bool forceSplit, IReadOnlyList<double>? manualProportions = null)
    {
        var local = _registry.ThisMachine;
        var localBudgetMb = local.Capabilities.MemoryMb;
        var requiredMb = model.EstimatedMemoryMb;

        // An unknown local capability is treated as fitting: refusing to run anything on a
        // machine we cannot measure would make the whole application unusable there.
        var fitsLocally = localBudgetMb == 0 || requiredMb <= localBudgetMb;

        if (fitsLocally && !forceSplit)
        {
            return PlanLocalOnly(model, requiredMb);
        }

        var remotes = _registry.RemoteSources
            .Where(s => s.State == SourceState.Available)
            .OrderBy(s => s.Locality)
            .ThenBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return remotes.Count == 0
            ? PlanWithGap(model, requiredMb, localBudgetMb, fitsLocally)
            : PlanSplit(model, requiredMb, remotes, local, manualProportions);
    }

    private CoveragePlan PlanLocalOnly(GgufModelInfo model, long requiredMb)
    {
        var section = new ModelSection(0, model.Name, model.Quantization, 0, model.LayerCount - 1);
        var redundancy = 1 + _registry.RemoteSources
            .Count(s => s.State == SourceState.Available
                && (s.Capabilities.MemoryMb == 0 || s.Capabilities.MemoryMb >= requiredMb));

        return new CoveragePlan(new[]
        {
            new SourceAssignment(section, _registry.ThisMachine, 1.0d, redundancy)
        });
    }

    /// <summary>
    /// The model needs more than this machine has and no other source is available. The plan
    /// still gets computed properly: the part this machine can hold, and an explicitly
    /// uncovered section for the remainder, which is what gates the run and what the panel
    /// points at.
    /// </summary>
    private CoveragePlan PlanWithGap(GgufModelInfo model, long requiredMb, long localBudgetMb, bool fitsLocally)
    {
        var localShare = fitsLocally
            ? 0.5d
            : Math.Clamp((double)localBudgetMb / requiredMb, MinimumShare, 1.0d - MinimumShare);

        var localLayers = Math.Clamp((int)Math.Round(model.LayerCount * localShare), 1, model.LayerCount - 1);

        var covered = new ModelSection(0, model.Name, model.Quantization, 0, localLayers - 1);
        var uncovered = new ModelSection(1, model.Name, model.Quantization, localLayers, model.LayerCount - 1);

        return new CoveragePlan(new[]
        {
            new SourceAssignment(covered, _registry.ThisMachine, localShare, 1),
            new SourceAssignment(uncovered, null, 1.0d - localShare, 0)
        });
    }

    private CoveragePlan PlanSplit(
        GgufModelInfo model,
        long requiredMb,
        List<InferenceSource> remotes,
        InferenceSource local,
        IReadOnlyList<double>? manualProportions)
    {
        // Remote sources first, this machine last, mirroring llama.cpp device registration
        // order so the tensor split proportions land on the devices they were meant for.
        var participants = remotes.Append(local).ToList();
        if (participants.Count > model.LayerCount)
        {
            participants = participants.Take(model.LayerCount).ToList();
        }

        var weights = ResolveWeights(participants, manualProportions);
        var layerCounts = AllocateLayers(model.LayerCount, weights);

        var assignments = new List<SourceAssignment>(participants.Count);
        var nextLayer = 0;
        for (var i = 0; i < participants.Count; i++)
        {
            var section = new ModelSection(
                i,
                model.Name,
                model.Quantization,
                nextLayer,
                nextLayer + layerCounts[i] - 1);
            nextLayer += layerCounts[i];

            var sectionMb = (long)Math.Ceiling(requiredMb * weights[i]);
            var redundancy = _registry.CandidatesForSection(sectionMb).Count();

            assignments.Add(new SourceAssignment(section, participants[i], weights[i], redundancy));
        }

        return new CoveragePlan(assignments);
    }

    private static double[] ResolveWeights(List<InferenceSource> participants, IReadOnlyList<double>? manualProportions)
    {
        double[] raw;
        if (manualProportions is not null
            && manualProportions.Count == participants.Count
            && manualProportions.All(p => p > 0))
        {
            raw = manualProportions.ToArray();
        }
        else
        {
            // Automatic by memory. Sources with an unknown capability get the average of the
            // known ones, or an equal share when nothing is known at all.
            var known = participants.Where(p => p.Capabilities.MemoryMb > 0).Select(p => (double)p.Capabilities.MemoryMb).ToList();
            var fallback = known.Count > 0 ? known.Average() : 1.0d;
            raw = participants
                .Select(p => p.Capabilities.MemoryMb > 0 ? p.Capabilities.MemoryMb : fallback)
                .ToArray();
        }

        var total = raw.Sum();
        return raw.Select(w => w / total).ToArray();
    }

    /// <summary>
    /// Turns proportions into whole contiguous layer counts that sum exactly to the model's
    /// layer count, with every participant getting at least one layer.
    /// </summary>
    private static int[] AllocateLayers(int layerCount, double[] weights)
    {
        var counts = new int[weights.Length];
        var remaining = layerCount;

        for (var i = 0; i < weights.Length; i++)
        {
            var participantsLeft = weights.Length - i;
            var ideal = (int)Math.Round(layerCount * weights[i]);
            counts[i] = Math.Clamp(ideal, 1, remaining - (participantsLeft - 1));
            remaining -= counts[i];
        }

        counts[^1] += remaining;
        return counts;
    }
}
