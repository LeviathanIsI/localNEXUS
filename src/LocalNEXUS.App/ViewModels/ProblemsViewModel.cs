using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Compilation;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The Problems panel: every compiler diagnostic the graph is currently reporting.
/// </summary>
/// <remarks>
/// Gathered from the compile check nodes rather than kept as a list of its own, so there is only
/// one copy of the truth and it is the one the node acted on. A node that repairs its code
/// successfully clears its own diagnostics, and this list empties with it, which is the behaviour
/// worth having: the panel says what is wrong now, not what was wrong at some point during the
/// run.
///
/// A compile check runs on the run's thread, not on the dispatcher, and publishes its diagnostics
/// from there. The binding engine marshals a property change on our behalf but not a change to a
/// collection it is bound to, so rebuilding this list where the notification arrived threw and
/// faulted the node every single run. It goes through the dispatcher for the same reason and in
/// the same shape as <see cref="Infrastructure.ActivityFeed"/>.
/// </remarks>
public sealed partial class ProblemsViewModel : ObservableObject, IDisposable
{
    private readonly GraphModel _graph;
    private readonly Dispatcher _dispatcher;
    private readonly HashSet<CompilerCheckNode> _observed = new();

    private bool _disposed;

    public ProblemsViewModel(GraphModel graph)
        : this(graph, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
    {
    }

    public ProblemsViewModel(GraphModel graph, Dispatcher dispatcher)
    {
        _graph = graph;
        _dispatcher = dispatcher;
        graph.Nodes.CollectionChanged += OnNodesChanged;

        Resubscribe();
    }

    /// <summary>Every diagnostic, errors first, then by file and line.</summary>
    public ObservableCollection<ProblemViewModel> Problems { get; } = new();

    /// <summary>How many there are, which is the count on the panel tab.</summary>
    public int Count => Problems.Count;

    /// <summary>How many of them stopped a compile.</summary>
    public int ErrorCount => Problems.Count(p => p.Severity == CompileSeverity.Error);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _graph.Nodes.CollectionChanged -= OnNodesChanged;

        foreach (var node in _observed)
        {
            node.PropertyChanged -= OnNodeChanged;
        }

        _observed.Clear();
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e) => Resubscribe();

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompilerCheckNode.LastProblems))
        {
            Rebuild();
        }
    }

    private void Resubscribe()
    {
        var wanted = _graph.Nodes.OfType<CompilerCheckNode>().ToHashSet();

        foreach (var gone in _observed.Except(wanted).ToList())
        {
            gone.PropertyChanged -= OnNodeChanged;
            _observed.Remove(gone);
        }

        foreach (var added in wanted.Except(_observed).ToList())
        {
            added.PropertyChanged += OnNodeChanged;
            _observed.Add(added);
        }

        Rebuild();
    }

    private void Rebuild()
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(Rebuild);
            return;
        }

        Problems.Clear();

        var rows = _graph.Nodes
            .OfType<CompilerCheckNode>()
            .SelectMany(node => node.LastProblems.Select(d => new ProblemViewModel(d, node.Title)))
            .OrderByDescending(p => p.Severity)
            .ThenBy(p => p.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Diagnostic.Line);

        foreach (var row in rows)
        {
            Problems.Add(row);
        }

        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(ErrorCount));
    }
}
