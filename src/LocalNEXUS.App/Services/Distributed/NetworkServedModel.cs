using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// One model the network can serve: an identity the mesh assigns, what it takes to run, and
/// how well the network covers it right now. Instances are updated in place by the manager, so
/// anything holding a reference, a list row or a model node, sees coverage change live.
/// </summary>
/// <remarks>
/// The identity is the mesh's model id, never a file path or a machine. Where the weights
/// physically live, which peer holds which layers, and whether any of it is on this machine
/// are all details of the current assembly rather than part of what the model is.
/// </remarks>
public sealed partial class NetworkServedModel : ObservableObject
{
    /// <summary>Quantization label reported in the model's metadata.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayLabel))]
    private string _quantization = "unknown";

    /// <summary>Number of transformer layers, which is what gets divided into sections.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequirementText))]
    private int _layerCount;

    /// <summary>Parameter count as the engine words it, for example <c>630M</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequirementText))]
    private string _parameterSize = string.Empty;

    /// <summary>Context window the model was loaded with.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequirementText))]
    private int _contextLength;

    /// <summary>The current assembly: who holds which section if this model ran now.</summary>
    [ObservableProperty]
    private CoveragePlan? _plan;

    /// <summary>The single most important signal: whether the mesh can serve this model right now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ChainStatusText))]
    [NotifyPropertyChangedFor(nameof(CanRun))]
    [NotifyPropertyChangedFor(nameof(HasDepth1))]
    [NotifyPropertyChangedFor(nameof(HasDepth2))]
    [NotifyPropertyChangedFor(nameof(HasDepth3))]
    private ModelAvailability _availability = ModelAvailability.Starting;

    /// <summary>
    /// What the section at fault, or the section still arriving, is doing. Null when complete.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChainStatusText))]
    private string? _statusDetail;

    /// <summary>How many distinct sources hold pieces of this model in the current assembly.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeerCountText))]
    private int _peerCount;

    /// <summary>Usable peers standing spare behind the weakest section.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength))]
    [NotifyPropertyChangedFor(nameof(ChainStatusText))]
    [NotifyPropertyChangedFor(nameof(HasDepth2))]
    [NotifyPropertyChangedFor(nameof(HasDepth3))]
    private int _weakestSpare;

    public NetworkServedModel(string modelId)
    {
        ModelId = modelId;
        Name = ShortenId(modelId);
    }

    /// <summary>The mesh's own id for this model. Sent as the model field on every request.</summary>
    public string ModelId { get; }

    /// <summary>The readable tail of the id, which is what the row leads with.</summary>
    public string Name { get; }

    /// <summary>Stable identity the manager reconciles on and graphs persist.</summary>
    public string ModelKey => ModelId;

    /// <summary>True only when the model can be run right now, which is what a node gates on.</summary>
    public bool CanRun => Availability == ModelAvailability.Complete;

    /// <summary>Overall strength: the weakest section decides, and a model still arriving has none yet.</summary>
    public SectionCoverage Strength => Availability switch
    {
        ModelAvailability.Blocked => SectionCoverage.Uncovered,
        ModelAvailability.Starting => SectionCoverage.Starting,
        _ => WeakestSpare >= 1 ? SectionCoverage.Healthy : SectionCoverage.Thin
    };

    /// <summary>One word for the row badge, with the detail carried separately.</summary>
    public string StatusText => Availability switch
    {
        ModelAvailability.Complete => "Complete",
        ModelAvailability.Blocked => "Blocked",
        _ => "Starting"
    };

    /// <summary>The sentence above the coverage chain.</summary>
    public string ChainStatusText => Availability switch
    {
        ModelAvailability.Complete => WeakestSpare > 0
            ? $"Complete and armed. Every section is serving, with {WeakestSpare} spare source(s) behind the weakest."
            : "Complete and armed. Every section is serving, with no spare source behind the weakest.",
        ModelAvailability.Blocked => StatusDetail ?? "Blocked: the mesh cannot assemble this model right now.",
        _ => StatusDetail ?? "Starting. Waiting for the mesh to report how this model is assembled."
    };

    /// <summary>First strength bar of the row: the model can run at all.</summary>
    public bool HasDepth1 => CanRun;

    /// <summary>Second strength bar: a spare source stands behind every section.</summary>
    public bool HasDepth2 => CanRun && WeakestSpare >= 1;

    /// <summary>Third strength bar: more than one spare source everywhere.</summary>
    public bool HasDepth3 => CanRun && WeakestSpare >= 2;

    /// <summary>What this model takes to run, from the metadata the mesh reports.</summary>
    public string RequirementText
    {
        get
        {
            var parts = new List<string>(3);

            if (!string.IsNullOrWhiteSpace(ParameterSize))
            {
                parts.Add($"{ParameterSize} parameters");
            }

            if (LayerCount > 0)
            {
                parts.Add($"{LayerCount} layers");
            }

            if (ContextLength > 0)
            {
                parts.Add($"{ContextLength:N0} token context");
            }

            return parts.Count == 0 ? "no metadata reported" : string.Join(", ", parts);
        }
    }

    public string PeerCountText => PeerCount == 1 ? "1 source" : $"{PeerCount} sources";

    /// <summary>Name plus quantization for dropdowns and the row title.</summary>
    public string DisplayLabel => $"{Name} ({Quantization})";

    /// <summary>
    /// Trims a mesh model id down to what is worth leading a row with. Ids are either a
    /// package reference with a namespace or a synthetic local id, and in both cases the
    /// last segment is the part a person recognises.
    /// </summary>
    private static string ShortenId(string modelId)
    {
        var lastSlash = modelId.LastIndexOf('/');
        var tail = lastSlash >= 0 && lastSlash < modelId.Length - 1
            ? modelId[(lastSlash + 1)..]
            : modelId;

        return string.IsNullOrWhiteSpace(tail) ? modelId : tail;
    }

    public override string ToString() => DisplayLabel;
}
