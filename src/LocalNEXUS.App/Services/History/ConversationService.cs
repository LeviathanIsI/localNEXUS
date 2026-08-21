using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.History;

/// <summary>
/// The running conversation for a project: what has been said, and what a run is waiting to hear.
/// </summary>
/// <remarks>
/// The graph is still the machine. This is how somebody drives it, which is what makes a follow up
/// like "use the existing slot rather than a new one" work without restating the request that
/// prompted it.
///
/// It is also what lets a node ask. A run that pauses for an answer is doing exactly what a run
/// that pauses for a confirmation already does: awaiting a task that the interface completes. The
/// executor is not involved in either, and does not learn that a node called Triage exists.
///
/// Turns are appended to the same database the runs go into, so the transcript and the record are
/// two views of one thing rather than two copies free to disagree.
/// </remarks>
public sealed partial class ConversationService : ObservableObject
{
    /// <summary>
    /// How long a question waits before the run gives up on it and proceeds.
    /// </summary>
    /// <remarks>
    /// Generous, because somebody who has gone to look at the project should not come back to a
    /// run that abandoned them. Bounded, because a run that waits forever is a run that has hung,
    /// and an assumption stated out loud is better than a window nobody can close.
    /// </remarks>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromMinutes(10);

    private readonly RunHistoryStore _store;
    private readonly Dispatcher _dispatcher;
    private readonly object _sync = new();

    private TaskCompletionSource<ClarificationOutcome>? _pending;
    private string _threadId = Guid.NewGuid().ToString();

    /// <summary>True while a run is waiting to be answered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    private bool _isAwaitingAnswer;

    /// <summary>What is being asked, while something is.</summary>
    [ObservableProperty]
    private string _questionText = string.Empty;

    public ConversationService(RunHistoryStore store)
        : this(store, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
    {
    }

    public ConversationService(RunHistoryStore store, Dispatcher dispatcher)
    {
        _store = store;
        _dispatcher = dispatcher;
    }

    /// <summary>The turns of the current thread, oldest first.</summary>
    public ObservableCollection<ConversationTurn> Turns { get; } = new();

    /// <summary>True when nothing is waiting on an answer.</summary>
    public bool IsIdle => !IsAwaitingAnswer;

    /// <summary>True when this thread has anything in it.</summary>
    public bool HasTurns => Turns.Count > 0;

    /// <summary>The conversation currently being talked in.</summary>
    public string ThreadId
    {
        get
        {
            lock (_sync)
            {
                return _threadId;
            }
        }
    }

    /// <summary>Reads back whichever conversation this project was left in the middle of.</summary>
    public async Task OpenProjectAsync(CancellationToken ct)
    {
        AbandonPending();

        var thread = await _store.ReadActiveThreadAsync(ct).ConfigureAwait(false);

        lock (_sync)
        {
            _threadId = thread;
        }

        var turns = await _store.ReadTurnsAsync(thread, RunHistoryStore.TranscriptLimit, ct).ConfigureAwait(false);
        Replace(turns);
    }

    /// <summary>
    /// Starts a fresh conversation, keeping every word of the old one.
    /// </summary>
    public void StartNew()
    {
        AbandonPending();

        var thread = _store.StartNewThread();

        lock (_sync)
        {
            _threadId = thread;
        }

        Replace(Array.Empty<ConversationTurn>());
    }

    /// <summary>
    /// Records something the person said, and answers whatever was waiting on it.
    /// </summary>
    /// <returns>True when this message answered a question rather than starting something new.</returns>
    public bool Say(string text, string? runId = null)
    {
        var turn = new ConversationTurn(
            Guid.NewGuid().ToString(),
            ThreadId,
            TurnRole.User,
            text,
            DateTimeOffset.Now,
            runId);

        Append(turn);

        // A message typed while a run is waiting is the answer to what it asked, not the start of
        // something else. That is the whole reason the questions go into the chat: there is one
        // place to type and it means the obvious thing.
        var pending = Interlocked.Exchange(ref _pending, null);

        if (pending is null)
        {
            return false;
        }

        IsAwaitingAnswer = false;
        QuestionText = string.Empty;
        pending.TrySetResult(new ClarificationOutcome(true, text));
        return true;
    }

    /// <summary>Records what the graph said back.</summary>
    public void Report(string text, string? runId = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Append(new ConversationTurn(
            Guid.NewGuid().ToString(),
            ThreadId,
            TurnRole.Graph,
            text,
            DateTimeOffset.Now,
            runId));
    }

    /// <summary>
    /// Puts questions into the chat and waits for the next thing the person says.
    /// </summary>
    /// <remarks>
    /// The same shape as asking for a confirmation, and for the same reason: the node awaits, the
    /// interface completes, and the run resumes exactly where it stopped rather than starting
    /// again. Nothing about this reaches the executor.
    /// </remarks>
    public async Task<ClarificationOutcome> AskAsync(
        string question,
        string? runId,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var completion = new TaskCompletionSource<ClarificationOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (Interlocked.CompareExchange(ref _pending, completion, null) is not null)
        {
            // Something is already waiting. Two questions outstanding at once would leave the
            // person with no way to say which one they are answering.
            return ClarificationOutcome.Unanswered;
        }

        Append(new ConversationTurn(
            Guid.NewGuid().ToString(),
            ThreadId,
            TurnRole.Question,
            question,
            DateTimeOffset.Now,
            runId));

        Invoke(() =>
        {
            QuestionText = question;
            IsAwaitingAnswer = true;
        });

        using var timer = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timer.Token);
        await using var registration = linked.Token.Register(() => completion.TrySetResult(ClarificationOutcome.Unanswered));

        var outcome = await completion.Task.ConfigureAwait(false);

        Interlocked.CompareExchange(ref _pending, null, completion);

        Invoke(() =>
        {
            IsAwaitingAnswer = false;
            QuestionText = string.Empty;
        });

        return outcome;
    }

    /// <summary>Lets the run carry on without an answer.</summary>
    public void ProceedWithoutAnswering()
    {
        var pending = Interlocked.Exchange(ref _pending, null);

        IsAwaitingAnswer = false;
        QuestionText = string.Empty;

        pending?.TrySetResult(ClarificationOutcome.Unanswered);
    }

    private void AbandonPending()
    {
        var pending = Interlocked.Exchange(ref _pending, null);

        IsAwaitingAnswer = false;
        QuestionText = string.Empty;

        pending?.TrySetResult(ClarificationOutcome.Unanswered);
    }

    private void Append(ConversationTurn turn)
    {
        _store.AppendTurn(turn);

        Invoke(() =>
        {
            Turns.Add(turn);
            OnPropertyChanged(nameof(HasTurns));
        });
    }

    private void Replace(IReadOnlyList<ConversationTurn> turns) => Invoke(() =>
    {
        Turns.Clear();

        foreach (var turn in turns)
        {
            Turns.Add(turn);
        }

        OnPropertyChanged(nameof(HasTurns));
    });

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
