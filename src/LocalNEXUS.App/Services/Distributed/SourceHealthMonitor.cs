using System.Net.Sockets;
using LocalNEXUS.App.Infrastructure;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// The application's one long lived background loop. Probes every remote source on a fixed
/// interval and keeps the registry's states current.
/// </summary>
/// <remarks>
/// Health lives as observable state on the sources and is rendered by the peer panel. Only a
/// genuine transition, available to unreachable or back, echoes a single line to the activity
/// feed; a heartbeat must never write there on every tick, because every feed entry is a
/// blocking hop onto the UI thread.
///
/// A probe is a TCP connect. rpc-server speaks a raw tensor protocol with no health endpoint,
/// so whether its socket accepts a connection is all the reachability information llama.cpp
/// exposes; this was verified against the bundled build.
/// </remarks>
public sealed class SourceHealthMonitor : IDisposable
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    private readonly SourceRegistry _registry;
    private readonly IActivityFeed _feed;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PeriodicTimer _timer = new(ProbeInterval);

    private Task? _loop;
    private bool _disposed;

    public SourceHealthMonitor(SourceRegistry registry, IActivityFeed feed)
    {
        _registry = registry;
        _feed = feed;
    }

    /// <summary>Starts the loop. Called once from the composition root.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_loop is not null)
        {
            return;
        }

        _loop = Task.Run(() => RunLoopAsync(_shutdown.Token));
    }

    /// <summary>
    /// Probes one source immediately, outside the regular cadence. Backs the probe now button
    /// in the panel and the pre launch check of the distributed request path.
    /// </summary>
    public async Task<bool> ProbeNowAsync(InferenceSource source, CancellationToken ct)
    {
        if (source.IsThisMachine)
        {
            return true;
        }

        return await ProbeAsync(source, ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _timer.Dispose();
        _shutdown.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            // The first sweep runs immediately so the panel shows real states at startup
            // instead of a wall of Unknown until the first interval elapses.
            await ProbeAllAsync(ct).ConfigureAwait(false);

            while (await _timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await ProbeAllAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task ProbeAllAsync(CancellationToken ct)
    {
        var targets = _registry.RemoteSources.ToList();
        if (targets.Count == 0)
        {
            return;
        }

        await Task.WhenAll(targets.Select(t => ProbeAsync(t, ct))).ConfigureAwait(false);
    }

    private async Task<bool> ProbeAsync(InferenceSource source, CancellationToken ct)
    {
        var settledState = source.State;
        source.State = SourceState.Probing;

        bool reachable;
        try
        {
            using var probeWindow = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeWindow.CancelAfter(ProbeTimeout);

            using var client = new TcpClient();
            await client.ConnectAsync(source.Host, source.Port, probeWindow.Token).ConfigureAwait(false);
            reachable = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            source.State = settledState;
            throw;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            reachable = false;
        }

        var newState = reachable ? SourceState.Available : SourceState.Unreachable;
        source.State = newState;
        source.RecordProbe(reachable);

        if (settledState != newState && settledState != SourceState.Probing)
        {
            _feed.Info(
                reachable ? "Source available" : "Source unreachable",
                $"{source.DisplayName} ({source.EndpointText}) is now {(reachable ? "available" : "unreachable")}.");
        }

        return reachable;
    }
}
