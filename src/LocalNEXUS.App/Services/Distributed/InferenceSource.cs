using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Anything that can hold a stage of a model: this machine's GPU, a machine on the LAN, or one
/// day a stranger over the internet. Sources are fungible; nothing here assumes a source is
/// local, trusted, or going to stay up.
/// </summary>
/// <remarks>
/// A source is now a mesh peer. Its identity is the peer's public key, which the engine
/// assigns and which survives sessions, so it is a better anchor for reputation than a
/// locally generated id ever was. Everything on this type is reported by the mesh; this
/// install does not probe, address, or manage the machine behind it.
/// </remarks>
public sealed partial class InferenceSource : ObservableObject
{
    /// <summary>Human label shown in the panel. Reported by the peer, falling back to its short id.</summary>
    [ObservableProperty]
    private string _displayName;

    /// <summary>The condition the mesh reports for this peer.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUsable))]
    private SourceState _state = SourceState.Unknown;

    /// <summary>Memory this peer announces, in MiB. Zero when it announces none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapabilityText))]
    private long _memoryMb;

    /// <summary>Round trip time the mesh last measured, in milliseconds. Null when unmeasured.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndpointText))]
    private int? _roundTripMs;

    /// <summary>How many models this peer is currently serving.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CapabilityText))]
    private int _servingModelCount;

    /// <summary>When this peer was last seen in a mesh report.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSeenText))]
    private DateTimeOffset? _lastSeenUtc;

    /// <summary>The engine version this peer runs, which matters for mixed version meshes.</summary>
    [ObservableProperty]
    private string _version = string.Empty;

    public InferenceSource(string sourceId, string displayName, SourceLocality locality)
    {
        SourceId = sourceId;
        _displayName = displayName;
        Locality = locality;
    }

    /// <summary>
    /// Stable identity that survives sessions: the peer's public key as the mesh reports it.
    /// This is what reputation will attach to later, so nothing here ever regenerates it.
    /// </summary>
    public string SourceId { get; }

    /// <summary>The first characters of the key, which is how the engine itself labels peers.</summary>
    public string ShortId => SourceId.Length <= 10 ? SourceId : SourceId[..10];

    /// <summary>Where the source sits. Ordering and display only, never correctness.</summary>
    public SourceLocality Locality { get; }

    /// <summary>
    /// Trust decision for this source. Always <see cref="SourceTrust.Trusted"/> today because a
    /// private mesh is joined by invitation, so every peer in it was let in deliberately.
    /// </summary>
    public SourceTrust Trust { get; init; } = SourceTrust.Trusted;

    /// <summary>True for the source that represents this install itself.</summary>
    public bool IsThisMachine => Locality == SourceLocality.ThisMachine;

    /// <summary>True when this peer could hold a stage right now.</summary>
    public bool IsUsable => State is SourceState.Available or SourceState.Serving;

    /// <summary>
    /// How the peer is reached. The mesh addresses peers by public key over its own transport,
    /// so there is no host and port to show; latency is the useful fact about the path instead.
    /// </summary>
    public string EndpointText => RoundTripMs is { } rtt
        ? $"{ShortId} at {rtt} ms"
        : ShortId;

    /// <summary>What this peer brings, for the source card.</summary>
    public string CapabilityText
    {
        get
        {
            var memory = MemoryMb > 0 ? $"{MemoryMb} MiB" : "memory not announced";
            return ServingModelCount switch
            {
                0 => memory,
                1 => $"{memory}, serving 1 model",
                _ => $"{memory}, serving {ServingModelCount} models"
            };
        }
    }

    /// <summary>Last seen time formatted for the panel.</summary>
    public string LastSeenText => LastSeenUtc is { } seen
        ? seen.ToLocalTime().ToString("HH:mm:ss")
        : "never";

    public override string ToString() => $"{DisplayName} ({ShortId})";
}
