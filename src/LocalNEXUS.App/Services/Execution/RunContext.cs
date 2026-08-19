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

    public RunContext(GraphModel graph, string userRequest)
    {
        Graph = graph;
        UserRequest = userRequest;
        StartedAt = DateTimeOffset.Now;
    }

    /// <summary>The graph being executed.</summary>
    public GraphModel Graph { get; }

    /// <summary>The text the user typed before pressing Run.</summary>
    public string UserRequest { get; }

    /// <summary>When the run began.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>True while nodes may execute, which excludes both paused and finished runs.</summary>
    public bool IsActive => State is RunState.Running or RunState.Paused;

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
