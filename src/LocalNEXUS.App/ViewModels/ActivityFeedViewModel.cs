using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The bottom panel: the transcript, the chat box, and the controls that drive a run.
/// </summary>
/// <remarks>
/// The run itself lives in <see cref="GraphExecutor"/>. This view model owns only the parts a
/// person interacts with: what was typed, whether Run is available, and cancelling or pausing.
/// Command availability is derived from <see cref="RunState"/> rather than from separate flags.
/// </remarks>
public sealed partial class ActivityFeedViewModel : ObservableObject
{
    private readonly GraphExecutor _executor;
    private readonly GraphModel _graph;
    private readonly ActivityFeed _feed;
    private readonly Dispatcher _dispatcher;
    private readonly RunCostTracker _cost;
    private readonly Services.Files.StagingStore _staging;

    private CancellationTokenSource? _runCancellation;
    private RunContext? _run;

    /// <summary>The request typed by the user, sent to input nodes when the run starts.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    private string _requestText = string.Empty;

    /// <summary>The lifecycle state of the current or most recent run.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsPaused))]
    [NotifyPropertyChangedFor(nameof(PauseButtonText))]
    private RunState _runState = RunState.Idle;

    public ActivityFeedViewModel(GraphExecutor executor, GraphModel graph, ActivityFeed feed)
        : this(executor, graph, feed, Dispatcher.CurrentDispatcher)
    {
    }

    /// <summary>
    /// True while the run controls are in front of the user.
    /// </summary>
    /// <remarks>
    /// A run belongs to the canvas, and the canvas is only on screen in the Workspace. Without
    /// this, the Run menu keeps working while the Network is showing and starts a run nobody can
    /// see, on a graph they are not looking at. The shell keeps it in step with the active view.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(ClearFeedCommand))]
    private bool _isActive = true;

    public ActivityFeedViewModel(
        GraphExecutor executor,
        GraphModel graph,
        ActivityFeed feed,
        Dispatcher dispatcher,
        RunCostTracker? cost = null,
        Services.Files.StagingStore? staging = null)
    {
        // The same store the output node writes to, so the box below is describing the files that
        // are actually waiting rather than a second copy of the idea.
        _staging = staging ?? new Services.Files.StagingStore(dispatcher);

        _executor = executor;
        _graph = graph;
        _feed = feed;
        _dispatcher = dispatcher;

        // The same instance the nodes add to, so the total the feed reports is the one they
        // built rather than a second count of the same thing.
        _cost = cost ?? new RunCostTracker();

        _executor.RunCreated += (_, run) => AttachRun(run);
    }

    /// <summary>The transcript, oldest entry first.</summary>
    public ObservableCollection<ActivityEvent> Events => _feed.Events;

    /// <summary>True while nodes are executing or the run is holding.</summary>
    public bool IsRunning => RunState is RunState.Running or RunState.Paused;

    /// <summary>True while the run is holding between nodes.</summary>
    public bool IsPaused => RunState == RunState.Paused;

    /// <summary>Label for the pause and resume button.</summary>
    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    /// <summary>The work the last run left behind, for the box to show and the next run to read.</summary>
    public Services.Files.StagingStore Staging => _staging;

    /// <summary>
    /// The request the run is given: what was typed, and what is still waiting.
    /// </summary>
    /// <remarks>
    /// This is how staged work is resolved from the chat box rather than by starting the whole
    /// request over. Somebody types what to do about the file that did not compile, and the run
    /// begins knowing which file that is, what it was for and what the compiler said, without
    /// anyone having to repeat it.
    ///
    /// Appended rather than substituted, and clearly labelled, so the typed request stays the
    /// request. Nothing is added when nothing is waiting.
    /// </remarks>
    private string ComposeRequest()
    {
        var typed = RequestText;

        if (!_staging.HasPending)
        {
            return typed;
        }

        return $"{typed}{Environment.NewLine}{Environment.NewLine}"
               + $"Work left unfinished by an earlier run, still waiting:{Environment.NewLine}"
               + _staging.Describe();
    }

    /// <summary>Forgets a staged file, because it is no longer wanted.</summary>
    [RelayCommand]
    private void DiscardStaged(Services.Files.StagedFile? file)
    {
        if (file is not null)
        {
            _staging.Resolve(file.RelativePath);
        }
    }

    /// <summary>Forgets everything that is waiting.</summary>
    [RelayCommand]
    private void DiscardAllStaged() => _staging.Clear();

    /// <summary>Runs the graph with the text currently in the chat box.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var request = ComposeRequest();

        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();

        _feed.Add(ActivityKind.Request, "Request", request);

        // Each run is priced on its own, so the total starts at nothing.
        _cost.Reset();

        RunState = RunState.Running;

        try
        {
            var run = await Task.Run(
                () => _executor.RunAsync(_graph, request, _runCancellation.Token),
                _runCancellation.Token).ConfigureAwait(true);

            RunState = run.State;
        }
        catch (OperationCanceledException)
        {
            RunState = RunState.Faulted;
        }
        catch (Exception ex)
        {
            _feed.Error("Run could not start", ex.Message);
            RunState = RunState.Faulted;
        }
        finally
        {
            // The final figure, once, and only when something actually cost money. A run made
            // entirely of local models says nothing rather than saying zero.
            if (_cost.HasCost)
            {
                _feed.Info(
                    "Run cost",
                    $"{RunCost.Format(_cost.Total)} across {_cost.Calls} call(s).");
            }

            DetachRun();
            _runCancellation?.Dispose();
            _runCancellation = null;
        }
    }

    /// <summary>Stops the run, cancelling the node that is currently executing.</summary>
    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        _run?.Resume();
        _runCancellation?.Cancel();
    }

    /// <summary>Holds the run before the next node, or releases a held run.</summary>
    [RelayCommand(CanExecute = nameof(CanTogglePause))]
    private void TogglePause()
    {
        if (_run is null)
        {
            return;
        }

        if (_run.State == RunState.Paused)
        {
            _run.Resume();
        }
        else
        {
            _run.Pause();
        }

        RunState = _run.State;
    }

    /// <summary>Empties the transcript.</summary>
    [RelayCommand(CanExecute = nameof(IsActive))]
    private void ClearFeed() => _feed.Clear();

    private bool CanRun() => IsActive && !IsRunning && !string.IsNullOrWhiteSpace(RequestText);

    private bool CanCancel() => IsActive && IsRunning;

    private bool CanTogglePause() => IsActive && IsRunning;

    /// <summary>
    /// Follows the run's own state so that a fault raised deep inside the executor reaches the
    /// buttons. The executor runs off the UI thread, so the update is marshalled back.
    /// </summary>
    private void AttachRun(RunContext run)
    {
        DetachRun();
        _run = run;
        run.PropertyChanged += OnRunPropertyChanged;
    }

    private void DetachRun()
    {
        if (_run is not null)
        {
            _run.PropertyChanged -= OnRunPropertyChanged;
        }
    }

    private void OnRunPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RunContext.State) || sender is not RunContext run)
        {
            return;
        }

        if (_dispatcher.CheckAccess())
        {
            RunState = run.State;
            return;
        }

        _dispatcher.BeginInvoke(() => RunState = run.State);
    }
}
