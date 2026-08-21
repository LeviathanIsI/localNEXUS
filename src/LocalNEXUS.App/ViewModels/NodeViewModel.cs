using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// One node as the canvas and the run outline draw it: what it is, how it is doing, and how long
/// it has been doing it.
/// </summary>
/// <remarks>
/// The node itself stays the source of truth and is not modified. Everything added here is either
/// derived from what the node already publishes or measured by watching it, which is what keeps
/// timing and the skipped state out of the execution model where they would be state nobody runs
/// on.
///
/// Elapsed time is measured rather than reported. The executor sets a node running and later sets
/// it completed, and the gap between those two notifications is the node's duration to within a
/// notification, which is far finer than a number rendered to a tenth of a second.
/// </remarks>
public sealed partial class NodeViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// Matches the fraction a node reports while working through a list, as in "2 of 3: Foo.cs"
    /// or "repair attempt 1 of 3". Read rather than invented: a bar that moves on a guess is
    /// worse than no bar.
    /// </summary>
    private static readonly Regex FractionPattern = new(
        @"(?<done>\d+)\s+of\s+(?<total>\d+)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Func<RunState> _runState;

    private long _startedTimestamp;
    private TimeSpan _finished;
    private bool _disposed;

    public NodeViewModel(NodeBase node, Func<RunState> runState)
    {
        Node = node;
        _runState = runState;

        node.PropertyChanged += OnNodePropertyChanged;
    }

    /// <summary>The node itself. The canvas binds its pins and position straight through.</summary>
    public NodeBase Node { get; }

    /// <summary>Title of the node, republished so the outline does not have to reach through.</summary>
    public string Title => Node.Title;

    /// <summary>The node type, which is what the accent colour is resolved from.</summary>
    public string TypeKey => Node.TypeKey;

    /// <summary>
    /// The type as a person would say it, which is not always the key.
    /// </summary>
    /// <remarks>
    /// Read from the palette rather than spelled here, for the same reason the inspector reads it
    /// from the palette: every extra place a type name is written out is a place it can come to
    /// disagree with the others, and this one used to show CompilerCheck under a node the palette
    /// calls Compiler check.
    /// </remarks>
    public string TypeLabel => Nodes.NodeFactory.Descriptors
        .FirstOrDefault(d => d.TypeKey == Node.TypeKey).DisplayName ?? Node.TypeKey;

    /// <summary>What the node last reported, such as a token rate or the file it is on.</summary>
    public string? Detail => Node.StatusMessage;

    /// <summary>True while this node is the canvas selection.</summary>
    public bool IsSelected
    {
        get => Node.IsSelected;
        set => Node.IsSelected = value;
    }

    /// <summary>
    /// How this node is drawn. Pending splits in two once a run has stopped: a node still pending
    /// after a fault was never reached, and says so rather than wearing the fault of the node
    /// that actually failed.
    /// </summary>
    public NodeDisplayState DisplayState => Node.State switch
    {
        NodeState.Running => NodeDisplayState.Running,
        NodeState.Completed => NodeDisplayState.Completed,
        NodeState.Faulted => NodeDisplayState.Faulted,
        _ => _runState() == RunState.Faulted ? NodeDisplayState.Skipped : NodeDisplayState.Pending
    };

    /// <summary>The state as one word, which is what both the node body and the outline show.</summary>
    public string StateText => DisplayState switch
    {
        NodeDisplayState.Running => "Running",
        NodeDisplayState.Completed => "Completed",
        NodeDisplayState.Faulted => "Faulted",
        NodeDisplayState.Skipped => "Skipped",
        _ => "Pending"
    };

    /// <summary>True while this node is the one executing.</summary>
    public bool IsRunning => DisplayState == NodeDisplayState.Running;

    /// <summary>True once this node has run, which is when an elapsed time is worth showing.</summary>
    public bool HasElapsed => Elapsed > TimeSpan.Zero;

    /// <summary>How long this node has been running, or how long it took.</summary>
    public TimeSpan Elapsed => _startedTimestamp == 0
        ? _finished
        : Stopwatch.GetElapsedTime(_startedTimestamp);

    /// <summary>Elapsed time as the node body shows it, counting up while it runs.</summary>
    public string ElapsedText
    {
        get
        {
            var elapsed = Elapsed;

            return elapsed >= TimeSpan.FromMinutes(1)
                ? elapsed.ToString(@"mm\:ss\.f", CultureInfo.InvariantCulture)
                : $"{elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s";
        }
    }

    /// <summary>
    /// How far through a list of work the node says it is, from zero to one, or null when it is
    /// doing something that has no countable steps.
    /// </summary>
    public double? Progress
    {
        get
        {
            if (!IsRunning || Detail is not { } detail)
            {
                return null;
            }

            var match = FractionPattern.Match(detail);
            if (!match.Success
                || !int.TryParse(match.Groups["done"].Value, out var done)
                || !int.TryParse(match.Groups["total"].Value, out var total)
                || total <= 0)
            {
                return null;
            }

            return Math.Clamp(done / (double)total, 0d, 1d);
        }
    }

    /// <summary>True when there is a countable fraction to draw a bar for.</summary>
    public bool HasProgress => Progress is not null;

    /// <summary>Progress as a percentage, which is what the bar binds to.</summary>
    public double ProgressPercent => (Progress ?? 0d) * 100d;

    /// <summary>
    /// Re-reads everything that depends on the run rather than on the node, which is how a run
    /// that faults turns every unreached node from pending to skipped without touching any of
    /// them.
    /// </summary>
    public void RefreshRunState()
    {
        OnPropertyChanged(nameof(DisplayState));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(IsRunning));
    }

    /// <summary>Re-reads the clock. Called by the document while a run is in flight.</summary>
    public void Tick()
    {
        if (_startedTimestamp == 0)
        {
            return;
        }

        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(HasElapsed));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Node.PropertyChanged -= OnNodePropertyChanged;
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(NodeBase.State):
                OnStateChanged();
                break;

            case nameof(NodeBase.StatusMessage):
                OnPropertyChanged(nameof(Detail));
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(HasProgress));
                OnPropertyChanged(nameof(ProgressPercent));
                break;

            case nameof(NodeBase.Title):
                OnPropertyChanged(nameof(Title));
                break;

            case nameof(NodeBase.IsSelected):
                OnPropertyChanged(nameof(IsSelected));
                break;
        }
    }

    private void OnStateChanged()
    {
        if (Node.State == NodeState.Running)
        {
            _startedTimestamp = Stopwatch.GetTimestamp();
        }
        else if (_startedTimestamp != 0)
        {
            _finished = Stopwatch.GetElapsedTime(_startedTimestamp);
            _startedTimestamp = 0;
        }
        else if (Node.State == NodeState.Pending)
        {
            // A reset before a new run. The previous run's duration is history, not this run's.
            _finished = TimeSpan.Zero;
        }

        RefreshRunState();
        Tick();
        OnPropertyChanged(nameof(Elapsed));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(HasElapsed));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(HasProgress));
        OnPropertyChanged(nameof(ProgressPercent));
    }
}
