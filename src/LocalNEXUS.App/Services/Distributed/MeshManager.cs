using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Owns this install's mesh node: the child process the distributed path runs on, and the
/// live picture of what the mesh can serve.
/// </summary>
/// <remarks>
/// The counterpart of <see cref="LlamaServerManager"/>, which still owns purely local
/// inference. Where that class starts a server for a model this machine holds, this one starts
/// a node that joins a mesh and lets the engine decide whether a model runs here, on a peer, or
/// as layer stages across several. Discovery, placement, transport and liveness are all the
/// engine's; this class starts the process, reads what the engine reports, and renders it.
///
/// The node is stopped by killing its process tree. The engine's own stop command tracks
/// instances through a runtime directory that a process started this way is not registered in,
/// so it reports nothing running and leaves the child alive; that was verified against the
/// bundled build rather than assumed.
/// </remarks>
public sealed partial class MeshManager : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan StartupGrace = TimeSpan.FromMinutes(3);

    private readonly AppConfig _config;
    private readonly IActivityFeed _feed;
    private readonly Dispatcher _dispatcher;
    private readonly MeshStatusReader _reader = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _shutdown;
    private Task? _pollLoop;
    private Process? _process;
    private StreamWriter? _log;
    private DateTimeOffset _startedAt;
    private bool _announcedReady;
    private bool _disposed;

    /// <summary>What this install's node is doing right now.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(CanJoinOrHost))]
    private MeshNodeState _state = MeshNodeState.Stopped;

    /// <summary>One line for the contribution card: what the node is doing.</summary>
    [ObservableProperty]
    private string _statusText = "Mesh node stopped";

    /// <summary>Friendly name of the mesh this node is in.</summary>
    [ObservableProperty]
    private string _meshName = string.Empty;

    /// <summary>The token another machine needs to join this mesh. Blank until the node hosts one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInviteToken))]
    private string _inviteToken = string.Empty;

    /// <summary>Whether the mesh is advertised publicly. False for the private default.</summary>
    [ObservableProperty]
    private bool _isPublic;

    /// <summary>True while this machine offers its own compute rather than only routing.</summary>
    [ObservableProperty]
    private bool _isContributing;

    /// <summary>Why the node is not running, when it failed. Null otherwise.</summary>
    [ObservableProperty]
    private string? _lastError;

    /// <summary>This install's own node, once the engine has reported its identity.</summary>
    [ObservableProperty]
    private InferenceSource? _thisMachine;

    public MeshManager(AppConfig config, IActivityFeed feed, Dispatcher dispatcher)
    {
        _config = config;
        _feed = feed;
        _dispatcher = dispatcher;
    }

    /// <summary>Every model the mesh can serve or is trying to. The Network tab's primary surface.</summary>
    public ObservableCollection<NetworkServedModel> Models { get; } = new();

    /// <summary>Every source in the mesh, this machine first. Populated entirely from mesh reports.</summary>
    public ObservableCollection<InferenceSource> Sources { get; } = new();

    /// <summary>True when a node process is up, whatever it is doing.</summary>
    public bool IsRunning => State is MeshNodeState.Starting or MeshNodeState.Client or MeshNodeState.Serving;

    /// <summary>True when membership settings can be edited, which is only while stopped.</summary>
    public bool CanJoinOrHost => State is MeshNodeState.Stopped or MeshNodeState.Failed;

    /// <summary>True once this node hosts a mesh that others can be invited into.</summary>
    public bool HasInviteToken => !string.IsNullOrWhiteSpace(InviteToken);

    /// <summary>Port the OpenAI compatible API listens on.</summary>
    public int ApiPort { get; private set; } = MeshLaunchOptions.DefaultApiPort;

    /// <summary>Port the management API answers on.</summary>
    public int ConsolePort { get; private set; } = MeshLaunchOptions.DefaultConsolePort;

    /// <summary>
    /// Where model nodes send requests. One endpoint for everything the mesh serves; which
    /// machine actually runs the model is the engine's business, not the graph's.
    /// </summary>
    public string ApiBaseUrl => $"http://127.0.0.1:{ApiPort}/v1";

    /// <summary>Finds a model by the identity a graph persisted. Null when the mesh no longer knows it.</summary>
    public NetworkServedModel? FindByKey(string? modelKey) => string.IsNullOrWhiteSpace(modelKey)
        ? null
        : Models.FirstOrDefault(m => string.Equals(m.ModelKey, modelKey, StringComparison.Ordinal));

    /// <summary>
    /// Starts the node if the user left it enabled. Failures are reported to the feed rather
    /// than allowed to interrupt composition.
    /// </summary>
    public async Task RestoreAsync()
    {
        if (!_config.MeshEnabled)
        {
            return;
        }

        try
        {
            await StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ModelClientException ex)
        {
            _feed.Error("Mesh node not started", ex.Message);
        }
    }

    /// <summary>Starts the node process and begins reading mesh state.</summary>
    /// <exception cref="ModelClientException">The executable is missing or Windows refused to start it.</exception>
    public async Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false })
            {
                return;
            }

            var options = BuildOptions();
            ApiPort = options.ApiPort;
            ConsolePort = options.ConsolePort;

            StartProcess(options);

            _shutdown = new CancellationTokenSource();
            _startedAt = DateTimeOffset.UtcNow;
            _announcedReady = false;

            State = MeshNodeState.Starting;
            IsContributing = options.Contribute;
            IsPublic = options.Publish;
            LastError = null;
            StatusText = options.Contribute
                ? "Starting, offering this machine to the mesh"
                : "Starting, joining as a client";

            _config.MeshEnabled = true;
            _config.Save();

            _pollLoop = Task.Run(() => PollLoopAsync(_shutdown.Token), CancellationToken.None);

            _feed.Info(
                "Mesh node starting",
                options.Contribute
                    ? $"Serving on port {options.ApiPort}, {(options.Publish ? "published publicly" : "private mesh on the local network")}."
                    : $"Joining as a client on port {options.ApiPort}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops the node and clears everything read from the mesh.</summary>
    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var wasRunning = _process is { HasExited: false };
            await ShutdownProcessAsync().ConfigureAwait(false);

            _config.MeshEnabled = false;
            _config.Save();

            await _dispatcher.InvokeAsync(() =>
            {
                Models.Clear();
                Sources.Clear();
                ThisMachine = null;
                State = MeshNodeState.Stopped;
                StatusText = "Mesh node stopped";
                MeshName = string.Empty;
                InviteToken = string.Empty;
                IsContributing = false;
            });

            if (wasRunning)
            {
                _feed.Info("Mesh node stopped", "This install left the mesh. Local inference is unaffected.");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _shutdown?.Cancel();

        // On exit the child is killed outright: nothing we start may outlive the window.
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // The process is already gone, which is the outcome we wanted.
        }

        _process?.Dispose();
        _log?.Dispose();
        _shutdown?.Dispose();
        _reader.Dispose();
        _gate.Dispose();
    }

    private MeshLaunchOptions BuildOptions() => new()
    {
        ApiPort = _config.MeshApiPort is >= 1 and <= 65535 ? _config.MeshApiPort : MeshLaunchOptions.DefaultApiPort,
        ConsolePort = _config.MeshConsolePort is >= 1 and <= 65535 ? _config.MeshConsolePort : MeshLaunchOptions.DefaultConsolePort,
        Contribute = _config.MeshContribute,
        OfferedModelPath = _config.MeshOfferedModelPath ?? string.Empty,
        MaxVramGb = Math.Max(0d, _config.MeshMaxVramGb),
        JoinToken = _config.MeshJoinToken ?? string.Empty,
        MeshName = string.IsNullOrWhiteSpace(_config.MeshName) ? "LocalNEXUS" : _config.MeshName,
        Publish = _config.MeshPublish
    };

    private void StartProcess(MeshLaunchOptions options)
    {
        var executable = AppPaths.FindMeshExecutable()
            ?? throw new ModelClientException(BuildMissingExecutableMessage());

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in options.BuildArguments(Environment.MachineName))
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new ModelClientException("Windows did not start the mesh node and gave no reason.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ModelClientException($"Could not start the mesh node: {ex.Message}", ex);
        }

        _process = process;
        _log = new StreamWriter(AppPaths.CreateLogFilePath("mesh"), append: true) { AutoFlush = true };

        process.OutputDataReceived += OnOutput;
        process.ErrorDataReceived += OnOutput;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private void OnOutput(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return;
        }

        try
        {
            _log?.WriteLine(e.Data);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Losing a log line must never take the node down with it.
        }
    }

    private async Task ShutdownProcessAsync()
    {
        _shutdown?.Cancel();

        if (_pollLoop is { } loop)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }

            _pollLoop = null;
        }

        if (_process is { } process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                // Already gone.
            }

            process.Dispose();
            _process = null;
        }

        _log?.Dispose();
        _log = null;

        _shutdown?.Dispose();
        _shutdown = null;
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            do
            {
                if (_process is { HasExited: true } exited)
                {
                    await ReportProcessDeathAsync(exited.ExitCode).ConfigureAwait(false);
                    return;
                }

                var snapshot = await _reader.ReadAsync(ConsolePort, ApiPort, ct).ConfigureAwait(false);

                if (snapshot is null)
                {
                    await ReportUnansweredAsync().ConfigureAwait(false);
                    continue;
                }

                await _dispatcher.InvokeAsync(() => Apply(snapshot));
            }
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task ReportProcessDeathAsync(int exitCode)
    {
        await _dispatcher.InvokeAsync(() =>
        {
            State = MeshNodeState.Failed;
            StatusText = $"Mesh node exited with code {exitCode}";
            LastError = $"The mesh node process exited with code {exitCode}. Its output is in the logs folder.";
            Models.Clear();
            Sources.Clear();
            ThisMachine = null;
        });

        _feed.Error(
            "Mesh node stopped unexpectedly",
            $"The node process exited with code {exitCode}. Local inference is unaffected; the distributed path is unavailable until it is started again.");
    }

    private async Task ReportUnansweredAsync()
    {
        // A node takes a while to answer while it resolves and loads a model, so silence is
        // only worth reporting once the startup grace has passed.
        if (State != MeshNodeState.Starting || DateTimeOffset.UtcNow - _startedAt < StartupGrace)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            StatusText = "Starting, the node has not answered its management API yet";
        });
    }

    /// <summary>
    /// Folds one snapshot into the observable state. Entries are updated in place and keyed by
    /// identity so that a list row, or a model node holding a reference, sees the change live.
    /// </summary>
    private void Apply(MeshSnapshot snapshot)
    {
        var previousState = State;

        MeshName = snapshot.MeshName;
        InviteToken = snapshot.InviteToken;
        IsPublic = string.Equals(snapshot.PublicationState, "public", StringComparison.OrdinalIgnoreCase);
        IsContributing = snapshot.IsServing;
        State = snapshot.IsServing ? MeshNodeState.Serving : MeshNodeState.Client;
        LastError = null;

        ReconcileSources(snapshot);
        ReconcileModels(snapshot);

        var complete = Models.Count(m => m.IsComplete);
        StatusText = State == MeshNodeState.Serving
            ? $"Serving in {DescribeMesh()}, {Sources.Count} source(s), {complete} model(s) ready"
            : $"Routing in {DescribeMesh()}, {Sources.Count} source(s), {complete} model(s) ready";

        // Only a genuine transition writes to the feed. A heartbeat must never write there on
        // every tick, because every entry is a blocking hop onto the UI thread.
        if (!_announcedReady && previousState == MeshNodeState.Starting)
        {
            _announcedReady = true;
            _feed.Info(
                "Mesh node ready",
                $"{DescribeMesh()} with {Sources.Count} source(s) and {complete} model(s) ready to serve.");
        }
    }

    private string DescribeMesh()
    {
        var name = string.IsNullOrWhiteSpace(MeshName) ? "an unnamed mesh" : $"mesh '{MeshName}'";
        return IsPublic ? $"{name} (public)" : $"{name} (private)";
    }

    private void ReconcileSources(MeshSnapshot snapshot)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(snapshot.NodeId))
        {
            var self = ThisMachine;
            if (self is null || !string.Equals(self.SourceId, snapshot.NodeId, StringComparison.Ordinal))
            {
                self = new InferenceSource(
                    snapshot.NodeId,
                    string.IsNullOrWhiteSpace(snapshot.ThisMachineName) ? Environment.MachineName : snapshot.ThisMachineName,
                    SourceLocality.ThisMachine);

                ThisMachine = self;
                Sources.Insert(0, self);
            }

            self.State = snapshot.IsServing ? SourceState.Serving : SourceState.Available;
            self.MemoryMb = snapshot.ThisMachineMemoryMb;
            self.ServingModelCount = snapshot.Models.Count(m => IsServedHere(m.Id, snapshot));
            self.LastSeenUtc = DateTimeOffset.UtcNow;
            seen.Add(self.SourceId);
        }

        foreach (var peer in snapshot.Peers)
        {
            seen.Add(peer.Id);

            var existing = Sources.FirstOrDefault(s => string.Equals(s.SourceId, peer.Id, StringComparison.Ordinal));
            if (existing is null)
            {
                existing = new InferenceSource(peer.Id, peer.DisplayName, SourceLocality.LocalNetwork);
                Sources.Add(existing);
                _feed.Info("Source joined", $"{peer.DisplayName} joined {DescribeMesh()}.");
            }

            existing.DisplayName = peer.DisplayName;
            existing.State = MapPeerState(peer);
            existing.MemoryMb = peer.MemoryMb;
            existing.RoundTripMs = peer.RoundTripMs;
            existing.ServingModelCount = peer.ServingModelIds.Count;
            existing.Version = peer.Version;
            existing.LastSeenUtc = DateTimeOffset.UtcNow;
        }

        foreach (var gone in Sources.Where(s => !seen.Contains(s.SourceId)).ToList())
        {
            Sources.Remove(gone);
            _feed.Info("Source left", $"{gone.DisplayName} is no longer in the mesh.");
        }
    }

    private static bool IsServedHere(string modelId, MeshSnapshot snapshot)
        => !snapshot.Peers.Any(p => p.ServingModelIds.Contains(modelId, StringComparer.Ordinal));

    private static SourceState MapPeerState(MeshPeer peer)
    {
        if (peer.ServingModelIds.Count > 0)
        {
            return SourceState.Serving;
        }

        return peer.State.ToLowerInvariant() switch
        {
            "disconnected" or "dead" or "unreachable" => SourceState.Unreachable,
            "" => SourceState.Unknown,
            _ => SourceState.Available
        };
    }

    private void ReconcileModels(MeshSnapshot snapshot)
    {
        var routable = snapshot.Models.ToDictionary(m => m.Id, StringComparer.Ordinal);

        var identities = routable.Keys
            .Concat(snapshot.AnnouncedModelIds)
            .Concat(snapshot.Stages.Select(s => s.ModelId))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var id in identities)
        {
            var entry = Models.FirstOrDefault(m => string.Equals(m.ModelId, id, StringComparison.Ordinal));
            if (entry is null)
            {
                entry = new NetworkServedModel(id);
                Models.Add(entry);
            }

            var isRoutable = routable.TryGetValue(id, out var model);
            if (isRoutable && model is not null)
            {
                entry.Quantization = model.Quantization;
                entry.LayerCount = model.LayerCount;
                entry.ParameterSize = model.ParameterSize;
                entry.ContextLength = model.ContextLength;
            }

            var plan = BuildPlan(id, entry.LayerCount, isRoutable, snapshot);

            entry.Plan = plan;
            entry.IsComplete = plan.IsComplete;
            entry.IncompleteReason = plan.IncompleteReason;
            entry.PeerCount = plan.SourceCount;
            entry.WeakestSpare = plan.WeakestSpare;
        }

        foreach (var stale in Models.Where(m => !identities.Contains(m.ModelId, StringComparer.Ordinal)).ToList())
        {
            Models.Remove(stale);
        }

        // Keep the list ordered by identity so rows do not jump around between polls.
        for (var target = 0; target < identities.Count; target++)
        {
            var current = Models.IndexOf(Models.First(m => string.Equals(m.ModelId, identities[target], StringComparison.Ordinal)));
            if (current != target)
            {
                Models.Move(current, target);
            }
        }
    }

    /// <summary>
    /// Reads the current assembly of one model out of the snapshot. A model the node can route
    /// to is complete by the engine's own contract: stage zero only becomes routable once every
    /// stage behind it reports ready.
    /// </summary>
    private CoveragePlan BuildPlan(string modelId, int layerCount, bool isRoutable, MeshSnapshot snapshot)
    {
        var stages = snapshot.Stages
            .Where(s => string.Equals(s.ModelId, modelId, StringComparison.Ordinal))
            .OrderBy(s => s.StageIndex)
            .ToList();

        var holders = stages
            .Select(s => ResolveSource(s.NodeId))
            .OfType<InferenceSource>()
            .Select(s => s.SourceId)
            .ToHashSet(StringComparer.Ordinal);

        if (stages.Count == 0)
        {
            var holder = ResolveSingleHolder(modelId, isRoutable, snapshot);
            if (holder is not null)
            {
                holders.Add(holder.SourceId);
            }

            var spare = CountSpareSources(holders);
            var section = new ModelSection(0, modelId, 0, Math.Max(0, layerCount - 1));

            return new CoveragePlan(new[]
            {
                new SourceAssignment(
                    section,
                    holder,
                    isRoutable,
                    isRoutable ? "serving" : holder is null ? "not placed" : "announced but not routable here",
                    spare)
            });
        }

        var spareForSplit = CountSpareSources(holders);

        var assignments = stages
            .Select((stage, ordinal) =>
            {
                var source = ResolveSource(stage.NodeId);
                var state = string.IsNullOrWhiteSpace(stage.State) ? "not reported" : stage.State;
                var ready = isRoutable || string.Equals(state, "ready", StringComparison.OrdinalIgnoreCase);

                return new SourceAssignment(
                    new ModelSection(ordinal, modelId, stage.FirstLayer, stage.LastLayer),
                    source,
                    ready,
                    state,
                    spareForSplit);
            })
            .ToList();

        return new CoveragePlan(assignments);
    }

    /// <summary>
    /// Matches a stage's node id to a source. Stage placements carry the full public key while
    /// peers are reported by a shortened one, so the match is by prefix in either direction.
    /// </summary>
    private InferenceSource? ResolveSource(string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        return Sources.FirstOrDefault(s =>
            s.SourceId.StartsWith(nodeId, StringComparison.OrdinalIgnoreCase)
            || nodeId.StartsWith(s.SourceId, StringComparison.OrdinalIgnoreCase));
    }

    private InferenceSource? ResolveSingleHolder(string modelId, bool isRoutable, MeshSnapshot snapshot)
    {
        var peer = snapshot.Peers.FirstOrDefault(p => p.ServingModelIds.Contains(modelId, StringComparer.Ordinal));
        if (peer is not null)
        {
            return ResolveSource(peer.Id);
        }

        return isRoutable ? ThisMachine : null;
    }

    /// <summary>
    /// Usable sources not already holding a piece of this model: the slack the mesh has to
    /// place a stage on if one of the current holders goes away.
    /// </summary>
    private int CountSpareSources(IReadOnlySet<string> holders)
        => Sources.Count(s => s.IsUsable && !holders.Contains(s.SourceId));

    private static string BuildMissingExecutableMessage()
    {
        var searched = string.Join(Environment.NewLine, AppPaths.EnumerateMeshSearchDirectories().Distinct());
        return $"{AppPaths.MeshExecutableName} was not found. Place a Mesh LLM build in vendor\\mesh. Searched:{Environment.NewLine}{searched}";
    }
}
