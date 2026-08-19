using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// One model the network can serve: an identity (name plus quantization, never a machine),
/// what it takes to run, and how well the network covers it right now. Instances are updated
/// in place by the index, so anything holding a reference, a list row or a model node, sees
/// coverage change live.
/// </summary>
public sealed partial class NetworkServedModel : ObservableObject
{
    /// <summary>Where this install can load the weights from. Empty once entries can exist that only the network holds.</summary>
    [ObservableProperty]
    private string _localPath = string.Empty;

    /// <summary>Size of the weights on disk.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText))]
    [NotifyPropertyChangedFor(nameof(RequirementText))]
    private long _fileBytes;

    /// <summary>Estimated memory to serve the whole model, in MiB.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RequirementText))]
    private long _estimatedMemoryMb;

    /// <summary>Number of transformer layers, which is what gets divided into sections.</summary>
    [ObservableProperty]
    private int _layerCount;

    /// <summary>The current assembly: who would fill which section if this model ran now.</summary>
    [ObservableProperty]
    private CoveragePlan? _plan;

    /// <summary>The single most important signal: whether every section has coverage.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ChainStatusText))]
    [NotifyPropertyChangedFor(nameof(HasDepth1))]
    [NotifyPropertyChangedFor(nameof(HasDepth2))]
    [NotifyPropertyChangedFor(nameof(HasDepth3))]
    private bool _isComplete;

    /// <summary>Why the model cannot run, naming the uncovered section. Null when complete.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(ChainStatusText))]
    private string? _incompleteReason;

    /// <summary>How many distinct sources serve pieces of this model in the current plan.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PeerCountText))]
    private int _peerCount;

    /// <summary>The redundancy of the weakest section: the chain is only as strong as this.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Strength))]
    [NotifyPropertyChangedFor(nameof(ChainStatusText))]
    [NotifyPropertyChangedFor(nameof(HasDepth1))]
    [NotifyPropertyChangedFor(nameof(HasDepth2))]
    [NotifyPropertyChangedFor(nameof(HasDepth3))]
    private int _weakestRedundancy;

    public NetworkServedModel(string name, string quantization)
    {
        Name = name;
        Quantization = quantization;
    }

    /// <summary>The model's own name from its metadata.</summary>
    public string Name { get; }

    /// <summary>Quantization label, part of the identity.</summary>
    public string Quantization { get; }

    /// <summary>Stable identity the index reconciles on and graphs persist.</summary>
    public string ModelKey => BuildKey(Name, Quantization);

    /// <summary>Overall strength: the weakest section decides.</summary>
    public SectionCoverage Strength => !IsComplete
        ? SectionCoverage.Uncovered
        : WeakestRedundancy >= 2 ? SectionCoverage.Healthy : SectionCoverage.Thin;

    /// <summary>One word for the row badge, with the reason carried separately.</summary>
    public string StatusText => IsComplete ? "Complete" : "Blocked";

    /// <summary>The sentence above the coverage chain.</summary>
    public string ChainStatusText => IsComplete
        ? $"Complete and armed. Every section is covered; the weakest has {WeakestRedundancy} candidate source(s)."
        : IncompleteReason ?? "Blocked: coverage is incomplete.";

    /// <summary>First strength bar of the row: the model can run at all.</summary>
    public bool HasDepth1 => IsComplete && WeakestRedundancy >= 1;

    /// <summary>Second strength bar: every section survives losing one source.</summary>
    public bool HasDepth2 => IsComplete && WeakestRedundancy >= 2;

    /// <summary>Third strength bar: comfortably covered everywhere.</summary>
    public bool HasDepth3 => IsComplete && WeakestRedundancy >= 3;

    public string SizeText => FileBytes switch
    {
        >= 1024L * 1024 * 1024 => $"{FileBytes / (1024d * 1024 * 1024):0.0} GB",
        >= 1024L * 1024 => $"{FileBytes / (1024d * 1024):0} MB",
        _ => $"{FileBytes} bytes"
    };

    public string RequirementText => $"{SizeText} on disk, about {EstimatedMemoryMb} MiB to serve";

    public string PeerCountText => PeerCount == 1 ? "1 source" : $"{PeerCount} sources";

    /// <summary>Name plus quantization for dropdowns and the row title.</summary>
    public string DisplayLabel => $"{Name}  ({Quantization})";

    public static string BuildKey(string name, string quantization) => $"{name}|{quantization}";

    public override string ToString() => DisplayLabel;
}
