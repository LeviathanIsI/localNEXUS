using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// What the network can serve, as a live list of models rather than a list of machines. The
/// network tab and the model node's network provider both bind to this directly, following
/// the ModelCatalog precedent.
/// </summary>
/// <remarks>
/// Today the population source is the models this install knows about, evaluated against the
/// sources in the registry, because no discovery protocol exists yet. Discovery replaces the
/// population path inside <see cref="Refresh"/> without changing the shape consumers see:
/// entries are reconciled in place by model identity, never rebuilt, so bound rows and node
/// selections survive every recomputation.
/// </remarks>
public sealed class NetworkModelIndex
{
    private readonly ModelCatalog _catalog;
    private readonly SourceRegistry _registry;
    private readonly CoveragePlanner _planner;
    private readonly Dispatcher _dispatcher;
    private readonly object _sync = new();
    private readonly Dictionary<string, CachedMetadata> _metadataCache = new(StringComparer.OrdinalIgnoreCase);

    public NetworkModelIndex(
        ModelCatalog catalog,
        SourceRegistry registry,
        CoveragePlanner planner,
        Dispatcher dispatcher)
    {
        _catalog = catalog;
        _registry = registry;
        _planner = planner;
        _dispatcher = dispatcher;

        _catalog.Models.CollectionChanged += OnCatalogChanged;
        _registry.Changed += OnRegistryChanged;

        Refresh();
    }

    /// <summary>The models the network can serve, ordered by name. Bound directly by the UI.</summary>
    public ObservableCollection<NetworkServedModel> Models { get; } = new();

    /// <summary>Finds an entry by its persisted identity, or null when the network no longer serves it.</summary>
    public NetworkServedModel? FindByKey(string? modelKey)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            return null;
        }

        lock (_sync)
        {
            return Models.FirstOrDefault(m => string.Equals(m.ModelKey, modelKey, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Recomputes every entry against the current sources. Metadata reads are cached by file
    /// identity, so after the first pass this is plan arithmetic only.
    /// </summary>
    public void Refresh()
    {
        var snapshot = new List<(GgufModelInfo Metadata, string Path)>();

        foreach (var model in _catalog.Models.ToList())
        {
            var metadata = ReadMetadataCached(model.Path);
            if (metadata is not null)
            {
                snapshot.Add((metadata, model.Path));
            }
        }

        var computed = new List<(GgufModelInfo Metadata, string Path, CoveragePlan Plan)>(snapshot.Count);
        foreach (var (metadata, path) in snapshot)
        {
            computed.Add((metadata, path, _planner.Plan(metadata, forceSplit: false)));
        }

        // Collection mutations land on the UI thread; everything above ran wherever the
        // trigger came from, which for registry changes is the health monitor's loop.
        _dispatcher.BeginInvoke(() => Reconcile(computed));
    }

    private void Reconcile(List<(GgufModelInfo Metadata, string Path, CoveragePlan Plan)> computed)
    {
        lock (_sync)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (metadata, path, plan) in computed.OrderBy(c => c.Metadata.Name, StringComparer.OrdinalIgnoreCase))
            {
                var key = NetworkServedModel.BuildKey(metadata.Name, metadata.Quantization);
                if (!seen.Add(key))
                {
                    continue;
                }

                var entry = Models.FirstOrDefault(m => string.Equals(m.ModelKey, key, StringComparison.Ordinal));
                if (entry is null)
                {
                    entry = new NetworkServedModel(metadata.Name, metadata.Quantization);
                    Models.Add(entry);
                }

                entry.LocalPath = path;
                entry.FileBytes = metadata.FileBytes;
                entry.EstimatedMemoryMb = metadata.EstimatedMemoryMb;
                entry.LayerCount = metadata.LayerCount;
                entry.Plan = plan;
                entry.IsComplete = plan.IsComplete;
                entry.IncompleteReason = plan.IncompleteReason;
                entry.PeerCount = plan.Assignments
                    .Where(a => a.Source is not null)
                    .Select(a => a.Source!.SourceId)
                    .Distinct()
                    .Count();
                entry.WeakestRedundancy = plan.WeakestAssignment.Redundancy;
            }

            foreach (var stale in Models.Where(m => !seen.Contains(m.ModelKey)).ToList())
            {
                Models.Remove(stale);
            }
        }
    }

    private GgufModelInfo? ReadMetadataCached(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return null;
            }

            var stamp = (file.LastWriteTimeUtc, file.Length);
            lock (_sync)
            {
                if (_metadataCache.TryGetValue(path, out var cached) && cached.Stamp == stamp)
                {
                    return cached.Metadata;
                }
            }

            var metadata = GgufMetadata.Read(path);
            lock (_sync)
            {
                _metadataCache[path] = new CachedMetadata(stamp, metadata);
            }

            return metadata;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // A file that cannot be read cannot be served; it simply does not appear.
            return null;
        }
    }

    private void OnCatalogChanged(object? sender, NotifyCollectionChangedEventArgs e) => Refresh();

    private void OnRegistryChanged(object? sender, EventArgs e) => Refresh();

    private sealed record CachedMetadata((DateTime, long) Stamp, GgufModelInfo Metadata);
}
