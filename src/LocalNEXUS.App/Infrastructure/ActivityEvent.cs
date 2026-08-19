using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace LocalNEXUS.App.Infrastructure;

/// <summary>
/// One entry in the activity feed.
/// </summary>
/// <remarks>
/// Model output arrives token by token, so the body of an entry is a growing buffer rather
/// than a fixed string. Change notifications for the body are coalesced to roughly thirty per
/// second: a fast local model can emit hundreds of tokens per second and one layout pass per
/// token would starve the UI thread for no visible benefit.
/// </remarks>
public sealed partial class ActivityEvent : ObservableObject
{
    private const int NotifyIntervalMilliseconds = 33;

    private readonly StringBuilder _body = new();
    private readonly object _sync = new();

    private long _lastNotifyTimestamp;

    /// <summary>True while the run is blocked waiting for the user to answer this entry.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    private bool _isAwaitingResponse;

    /// <summary>How a confirmation entry was answered, once it has been answered.</summary>
    [ObservableProperty]
    private string? _resolution;

    /// <summary>Trailing detail such as a token count or an elapsed time, shown right aligned.</summary>
    [ObservableProperty]
    private string? _detail;

    private TaskCompletionSource<bool>? _completion;

    public ActivityEvent(ActivityKind kind, string title, string? text = null, Guid? nodeId = null)
    {
        Kind = kind;
        Title = title;
        NodeId = nodeId;
        Timestamp = DateTimeOffset.Now;

        if (!string.IsNullOrEmpty(text))
        {
            _body.Append(text);
        }
    }

    /// <summary>What kind of entry this is.</summary>
    public ActivityKind Kind { get; }

    /// <summary>The headline of the entry, for example the node title.</summary>
    public string Title { get; }

    /// <summary>The node this entry belongs to, when it belongs to one.</summary>
    public Guid? NodeId { get; }

    /// <summary>When the entry was created.</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>Clock time for display.</summary>
    public string TimeText => Timestamp.ToString("HH:mm:ss");

    /// <summary>The body of the entry. Grows as tokens stream in.</summary>
    public string Text
    {
        get
        {
            lock (_sync)
            {
                return _body.ToString();
            }
        }
    }

    /// <summary>True when the entry has a body worth rendering.</summary>
    public bool HasText
    {
        get
        {
            lock (_sync)
            {
                return _body.Length > 0;
            }
        }
    }

    /// <summary>Answers a confirmation entry with yes.</summary>
    [RelayCommand(CanExecute = nameof(CanRespond))]
    private void Confirm() => Respond(true, "Confirmed");

    /// <summary>Answers a confirmation entry with no.</summary>
    [RelayCommand(CanExecute = nameof(CanRespond))]
    private void Cancel() => Respond(false, "Cancelled");

    /// <summary>Appends streamed content to the body of the entry.</summary>
    public void Append(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return;
        }

        lock (_sync)
        {
            _body.Append(chunk);
        }

        var now = Environment.TickCount64;
        if (now - _lastNotifyTimestamp < NotifyIntervalMilliseconds)
        {
            return;
        }

        _lastNotifyTimestamp = now;
        RaiseTextChanged();
    }

    /// <summary>Replaces the body of the entry outright.</summary>
    public void SetText(string text)
    {
        lock (_sync)
        {
            _body.Clear();
            _body.Append(text);
        }

        RaiseTextChanged();
    }

    /// <summary>
    /// Forces a change notification. Called once a stream ends so that the last few tokens,
    /// which may have fallen inside the coalescing window, still reach the view.
    /// </summary>
    public void Flush()
    {
        _lastNotifyTimestamp = Environment.TickCount64;
        RaiseTextChanged();
    }

    /// <summary>
    /// Turns this entry into a question and returns a task that completes when the user answers.
    /// </summary>
    internal Task<bool> BeginConfirmation()
    {
        _completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsAwaitingResponse = true;
        return _completion.Task;
    }

    /// <summary>Answers an outstanding question without user interaction, used when a run is cancelled.</summary>
    internal void AbandonConfirmation(string reason)
    {
        if (!IsAwaitingResponse)
        {
            return;
        }

        Respond(false, reason);
    }

    private bool CanRespond() => IsAwaitingResponse;

    private void Respond(bool answer, string resolution)
    {
        if (!IsAwaitingResponse)
        {
            return;
        }

        IsAwaitingResponse = false;
        Resolution = resolution;
        _completion?.TrySetResult(answer);
    }

    private void RaiseTextChanged()
    {
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(HasText));
    }
}
