using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace LocalNEXUS.App.Infrastructure;

/// <summary>
/// The live transcript of a run, bound to the panel at the bottom of the window.
/// </summary>
/// <remarks>
/// Nodes execute on background threads, so every mutation of <see cref="Events"/> is marshalled
/// to the dispatcher. Individual entries handle their own notifications: WPF marshals scalar
/// property changes for us, but observable collections have to be touched on the UI thread.
/// </remarks>
public sealed class ActivityFeed : IActivityFeed
{
    private readonly Dispatcher _dispatcher;

    public ActivityFeed()
        : this(Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
    {
    }

    public ActivityFeed(Dispatcher dispatcher) => _dispatcher = dispatcher;

    /// <summary>
    /// Told about every entry as it is added, and again once a streamed one has finished.
    /// </summary>
    /// <remarks>
    /// A hook rather than a dependency. The feed reports what happened and has no idea whether
    /// anything is writing it down, which is what keeps recording out of the path a node takes to
    /// say something.
    /// </remarks>
    public Action<ActivityEvent, bool>? Recorder { get; set; }

    /// <summary>Every entry recorded so far, oldest first.</summary>
    public ObservableCollection<ActivityEvent> Events { get; } = new();

    /// <inheritdoc />
    public ActivityEvent Add(ActivityKind kind, string title, string? text = null, Guid? nodeId = null)
    {
        var entry = new ActivityEvent(kind, title, text, nodeId);

        var recorder = Recorder;

        if (recorder is not null)
        {
            entry.Completed = finished => recorder(finished, true);
            recorder(entry, false);
        }

        Invoke(() => Events.Add(entry));
        return entry;
    }

    /// <inheritdoc />
    public ActivityEvent Info(string title, string? text = null) => Add(ActivityKind.Info, title, text);

    /// <inheritdoc />
    public ActivityEvent Error(string title, string? text = null) => Add(ActivityKind.Error, title, text);

    /// <inheritdoc />
    public async Task<bool> RequestConfirmationAsync(string title, string message, CancellationToken ct)
    {
        var entry = Add(ActivityKind.Confirmation, title, message);

        Task<bool> answer = null!;
        Invoke(() => answer = entry.BeginConfirmation());

        await using var registration = ct.Register(() =>
            Invoke(() => entry.AbandonConfirmation("Cancelled because the run stopped")));

        return await answer.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Clear() => Invoke(Events.Clear);

    private void Invoke(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.Invoke(action);
    }
}
