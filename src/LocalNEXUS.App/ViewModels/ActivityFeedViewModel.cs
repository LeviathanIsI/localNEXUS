using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;

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

    public ActivityFeedViewModel(GraphExecutor executor, GraphModel graph, ActivityFeed feed, Dispatcher dispatcher)
    {
        _executor = executor;
        _graph = graph;
        _feed = feed;
        _dispatcher = dispatcher;

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

    /// <summary>Runs the graph with the text currently in the chat box.</summary>
    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var request = RequestText;

        _runCancellation?.Dispose();
        _runCancellation = new CancellationTokenSource();

        _feed.Add(ActivityKind.Request, "Request", request);
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
