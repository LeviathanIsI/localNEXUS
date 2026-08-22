using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// The state of one run: which node is executing, what each pin has produced so far, and
/// whether the run is paused.
/// </summary>
public sealed partial class RunContext : ObservableObject
{
    private readonly Dictionary<Guid, object?> _pinValues = new();
    private readonly Dictionary<Guid, object?> _wireValues = new();
    private readonly List<RunDecision> _decisions = new();
    private readonly object _sync = new();

    /// <summary>Where the run currently is in its lifecycle.</summary>
    [ObservableProperty]
    private RunState _state = RunState.Idle;

    /// <summary>The node being executed, or null between nodes.</summary>
    [ObservableProperty]
    private NodeBase? _currentNode;

    /// <summary>Set when the run ends badly, so the UI can surface the reason.</summary>
    [ObservableProperty]
    private string? _faultMessage;

    private TaskCompletionSource? _pauseGate;

    public RunContext(GraphModel graph, string userRequest, string? runId = null)
    {
        Graph = graph;
        UserRequest = userRequest;
        StartedAt = DateTimeOffset.Now;
        RunId = runId;
    }

    /// <summary>
    /// This run's identity in the record, or null when nothing is recording.
    /// </summary>
    /// <remarks>
    /// Carried here so a node can file a snapshot or a written file under the run it belongs to
    /// without going looking for what is currently in progress.
    /// </remarks>
    public string? RunId { get; }

    /// <summary>The graph being executed.</summary>
    public GraphModel Graph { get; }

    /// <summary>The text the user typed before pressing Run.</summary>
    public string UserRequest { get; }

    /// <summary>When the run began.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>True while nodes may execute, which excludes both paused and finished runs.</summary>
    public bool IsActive => State is RunState.Running or RunState.Paused;

    /// <summary>
    /// Every judgement this run made that nothing else records, in the order it made them.
    /// </summary>
    /// <remarks>
    /// A refusal to create a second copy of an existing type, and a refusal to write something
    /// that would silently break a scene, are the two things this application is for. Both used to
    /// exist only as sentences in the activity feed, which reads well and cannot be counted, so
    /// anything asking how often either happened got the same answer whether or not it ever had.
    /// </remarks>
    public IReadOnlyList<RunDecision> Decisions
    {
        get
        {
            lock (_sync)
            {
                return _decisions.ToList();
            }
        }
    }

    /// <summary>Adds one decision to the run's record.</summary>
    public void Record(RunDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        lock (_sync)
        {
            _decisions.Add(decision);
        }
    }

    /// <summary>Records the value a node produced on one of its output pins.</summary>
    public void SetValue(Pin pin, object? value)
    {
        lock (_sync)
        {
            _pinValues[pin.Id] = value;
        }
    }

    /// <summary>Reads a value previously produced on a pin.</summary>
    public bool TryGetValue(Pin pin, out object? value)
    {
        lock (_sync)
        {
            return _pinValues.TryGetValue(pin.Id, out value);
        }
    }

    /// <summary>
    /// Records what one wire is to carry, which is not always what its source pin produced.
    /// </summary>
    /// <remarks>
    /// Keyed by the wire's target pin rather than by its source, and that is the whole reason this
    /// exists separately from the pin values. One output pin can feed several inputs, and somebody
    /// who edits a value at a breakpoint has edited what that wire carries, not what the node
    /// produced. Writing it back over the pin would change what every other wire out of that pin
    /// delivers, silently.
    ///
    /// An input pin has at most one incoming wire, so the target identifies the wire.
    /// </remarks>
    public void SetWireValue(Connection connection, object? value)
    {
        ArgumentNullException.ThrowIfNull(connection);

        lock (_sync)
        {
            _wireValues[connection.TargetPinId] = value;
        }
    }

    /// <summary>Reads what a wire was told to carry, if anything overrode it.</summary>
    public bool TryGetWireValue(Connection connection, out object? value)
    {
        ArgumentNullException.ThrowIfNull(connection);

        lock (_sync)
        {
            return _wireValues.TryGetValue(connection.TargetPinId, out value);
        }
    }

    /// <summary>Requests that the run holds before the next node starts.</summary>
    public void Pause()
    {
        if (State != RunState.Running)
        {
            return;
        }

        Interlocked.CompareExchange(
            ref _pauseGate,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously),
            null);

        State = RunState.Paused;
    }

    /// <summary>Releases a paused run.</summary>
    public void Resume()
    {
        if (State != RunState.Paused)
        {
            return;
        }

        State = RunState.Running;
        Interlocked.Exchange(ref _pauseGate, null)?.TrySetResult();
    }

    /// <summary>
    /// Completes once the run is not paused. The executor awaits this between nodes, which is
    /// why pausing never interrupts a model mid stream.
    /// </summary>
    public Task WaitWhilePausedAsync(CancellationToken ct)
    {
        var gate = Volatile.Read(ref _pauseGate);
        return gate is null ? Task.CompletedTask : gate.Task.WaitAsync(ct);
    }

    /// <summary>Releases any paused state so a cancelled run can unwind.</summary>
    internal void ReleasePauseGate()
        => Interlocked.Exchange(ref _pauseGate, null)?.TrySetResult();
}
