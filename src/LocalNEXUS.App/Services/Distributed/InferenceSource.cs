using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Anything that can fill a model section: this machine's GPU, a machine on the LAN, or one
/// day a stranger over the internet. Sources are fungible; nothing here assumes a source is
/// local, trusted, or going to stay up.
/// </summary>
public sealed partial class InferenceSource : ObservableObject
{
    private const int ReachabilityWindow = 20;

    private readonly object _sync = new();
    private readonly Queue<bool> _recentProbes = new();

    /// <summary>Human label shown in the panel.</summary>
    [ObservableProperty]
    private string _displayName;

    /// <summary>Host name or address the source's rpc-server listens on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndpointText))]
    private string _host;

    /// <summary>Port the source's rpc-server listens on.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EndpointText))]
    private int _port;

    /// <summary>The observed condition, driven by the health monitor.</summary>
    [ObservableProperty]
    private SourceState _state = SourceState.Unknown;

    /// <summary>When a probe last reached this source. Null until the first success.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LastSeenText))]
    private DateTimeOffset? _lastSeenUtc;

    /// <summary>Rolling reachability over the recent probe window, for the panel and later for reputation.</summary>
    [ObservableProperty]
    private string _reachabilitySummary = "not probed yet";

    public InferenceSource(Guid sourceId, string displayName, string host, int port, SourceLocality locality)
    {
        SourceId = sourceId;
        _displayName = displayName;
        _host = host;
        _port = port;
        Locality = locality;
        Capabilities = new SourceCapabilities();
    }

    /// <summary>
    /// Stable identity that survives sessions. This is what reputation will attach to later,
    /// so it is generated once per install and never regenerated.
    /// </summary>
    public Guid SourceId { get; }

    /// <summary>Where the source sits. Ordering and display only, never correctness.</summary>
    public SourceLocality Locality { get; }

    /// <summary>
    /// Trust decision for this source. Always <see cref="SourceTrust.Trusted"/> today because
    /// every registered source is the user's own machine.
    /// </summary>
    public SourceTrust Trust { get; init; } = SourceTrust.Trusted;

    /// <summary>What this source can serve.</summary>
    public SourceCapabilities Capabilities { get; }

    /// <summary>True for the source that represents this install itself.</summary>
    public bool IsThisMachine => Locality == SourceLocality.ThisMachine;

    /// <summary>
    /// The endpoint in the form llama-server's --rpc flag expects. This machine shows only its
    /// host until it is actually serving a port.
    /// </summary>
    public string EndpointText => Port > 0 ? $"{Host}:{Port}" : Host;

    /// <summary>Last seen time formatted for the panel.</summary>
    public string LastSeenText => LastSeenUtc is { } seen
        ? seen.ToLocalTime().ToString("HH:mm:ss")
        : "never";

    /// <summary>
    /// Records a probe outcome into the rolling reachability window and stamps the last seen
    /// time on success. Called by the health monitor from its background loop.
    /// </summary>
    public void RecordProbe(bool reachable)
    {
        string summary;
        lock (_sync)
        {
            _recentProbes.Enqueue(reachable);
            while (_recentProbes.Count > ReachabilityWindow)
            {
                _recentProbes.Dequeue();
            }

            var reached = _recentProbes.Count(p => p);
            summary = $"{reached}/{_recentProbes.Count} recent probes ok";
        }

        if (reachable)
        {
            LastSeenUtc = DateTimeOffset.UtcNow;
        }

        ReachabilitySummary = summary;
    }

    public override string ToString() => $"{DisplayName} ({EndpointText})";
}
