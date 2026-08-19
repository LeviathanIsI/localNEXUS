using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Every source this install knows about, this machine always included. The UI binds to
/// <see cref="Sources"/> directly, following the same pattern as the model catalog. Entries
/// are populated from the configuration file; the registry does not know or care how they
/// arrived there.
/// </summary>
public sealed partial class SourceRegistry : ObservableObject
{
    private readonly AppConfig _config;

    public SourceRegistry(AppConfig config)
    {
        _config = config;

        // The stable identity reputation will attach to later. Generated exactly once per
        // install and persisted immediately, never regenerated.
        if (_config.SourceId == Guid.Empty)
        {
            _config.SourceId = Guid.NewGuid();
            _config.Save();
        }

        ThisMachine = new InferenceSource(
            _config.SourceId,
            Environment.MachineName,
            "127.0.0.1",
            0,
            SourceLocality.ThisMachine)
        {
            State = SourceState.Available
        };

        ThisMachine.Capabilities.MemoryMb = _config.ThisMachineMemoryMb > 0
            ? _config.ThisMachineMemoryMb
            : GpuMemoryProbe.TryReadTotalMemoryMb();

        Sources.Add(ThisMachine);
        Observe(ThisMachine);

        foreach (var record in _config.KnownSources)
        {
            var source = Hydrate(record);
            if (source is not null)
            {
                Sources.Add(source);
                Observe(source);
            }
        }
    }

    /// <summary>Raised when a source is added or removed, or when any source's state or capability changes.</summary>
    public event EventHandler? Changed;

    /// <summary>All known sources, this machine first. The panel binds this directly.</summary>
    public ObservableCollection<InferenceSource> Sources { get; } = new();

    /// <summary>The source representing this install. Always present, never removable.</summary>
    public InferenceSource ThisMachine { get; }

    /// <summary>Every source other than this machine, in registration order.</summary>
    public IEnumerable<InferenceSource> RemoteSources => Sources.Where(s => !s.IsThisMachine);

    /// <summary>
    /// The sources that could cover a section of the given estimated size right now: available,
    /// and either declaring enough memory or declaring none, since an unknown capability cannot
    /// be ruled out.
    /// </summary>
    public IEnumerable<InferenceSource> CandidatesForSection(long estimatedSectionMb) => Sources
        .Where(s => s.State == SourceState.Available)
        .Where(s => s.Capabilities.MemoryMb == 0 || s.Capabilities.MemoryMb >= estimatedSectionMb);

    /// <summary>
    /// Registers a source. Returns null and does nothing when the input is invalid or the
    /// endpoint is already registered.
    /// </summary>
    public InferenceSource? AddSource(string displayName, string host, int port, SourceLocality locality, long memoryMb)
    {
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        {
            return null;
        }

        var trimmedHost = host.Trim();
        if (Sources.Any(s => string.Equals(s.Host, trimmedHost, StringComparison.OrdinalIgnoreCase) && s.Port == port))
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(displayName) ? $"{trimmedHost}:{port}" : displayName.Trim();
        var source = new InferenceSource(Guid.NewGuid(), name, trimmedHost, port, locality);
        source.Capabilities.MemoryMb = Math.Max(0, memoryMb);

        Sources.Add(source);
        Observe(source);
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
        return source;
    }

    /// <summary>Removes a source. This machine cannot be removed.</summary>
    public bool RemoveSource(InferenceSource source)
    {
        if (source.IsThisMachine || !Sources.Remove(source))
        {
            return false;
        }

        Unobserve(source);
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Writes the current sources back to the configuration file. Called after edits made
    /// through the panel so a change to a host or a declared memory survives a restart.
    /// </summary>
    public void Persist()
    {
        _config.KnownSources = RemoteSources
            .Select(s => new KnownSourceRecord
            {
                SourceId = s.SourceId,
                DisplayName = s.DisplayName,
                Host = s.Host,
                Port = s.Port,
                Locality = s.Locality.ToString(),
                MemoryMb = s.Capabilities.MemoryMb
            })
            .ToList();

        _config.ThisMachineMemoryMb = ThisMachine.Capabilities.MemoryMb;
        _config.Save();
    }

    private InferenceSource? Hydrate(KnownSourceRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Host) || record.Port is < 1 or > 65535)
        {
            return null;
        }

        var locality = Enum.TryParse<SourceLocality>(record.Locality, out var parsed) && parsed != SourceLocality.ThisMachine
            ? parsed
            : SourceLocality.LocalNetwork;

        var id = record.SourceId == Guid.Empty ? Guid.NewGuid() : record.SourceId;
        var name = string.IsNullOrWhiteSpace(record.DisplayName) ? $"{record.Host}:{record.Port}" : record.DisplayName;

        var source = new InferenceSource(id, name, record.Host, record.Port, locality);
        source.Capabilities.MemoryMb = Math.Max(0, record.MemoryMb);
        return source;
    }

    private void Observe(InferenceSource source)
    {
        source.PropertyChanged += OnSourcePropertyChanged;
        source.Capabilities.PropertyChanged += OnSourcePropertyChanged;
    }

    private void Unobserve(InferenceSource source)
    {
        source.PropertyChanged -= OnSourcePropertyChanged;
        source.Capabilities.PropertyChanged -= OnSourcePropertyChanged;
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(InferenceSource.State) or nameof(SourceCapabilities.MemoryMb))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // Edits made in the panel survive a restart without a separate save step.
        if (e.PropertyName is nameof(InferenceSource.DisplayName)
            or nameof(InferenceSource.Host)
            or nameof(InferenceSource.Port)
            or nameof(SourceCapabilities.MemoryMb))
        {
            Persist();
        }
    }
}
