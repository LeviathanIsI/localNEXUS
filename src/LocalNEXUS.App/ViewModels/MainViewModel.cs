using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The window: the canvas, the palette, the settings panel, and the File menu.
/// </summary>
/// <remarks>
/// Canvas selection is tracked through each node's own <see cref="NodeBase.IsSelected"/> flag,
/// which the item container binds two way. That keeps the settings panel in step with the canvas
/// without the view model reaching into the visual tree.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private const double CascadeStep = 36d;

    private readonly NodeFactory _factory;
    private readonly GraphSerializer _serializer;
    private readonly IDialogService _dialogs;
    private readonly ActivityFeed _feed;
    private readonly AppConfig _config;

    /// <summary>Nodes whose selection state this view model is currently following.</summary>
    private readonly HashSet<NodeBase> _observedNodes = new();

    private int _cascadeIndex;

    /// <summary>The node whose settings the right hand panel is showing, or null when nothing is selected.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectionCommand))]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private NodeBase? _selectedNode;

    /// <summary>Path of the graph currently open, or null when it has never been saved.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string? _currentGraphPath;

    public MainViewModel(
        GraphModel graph,
        NodeFactory factory,
        GraphSerializer serializer,
        IDialogService dialogs,
        ActivityFeed feed,
        ActivityFeedViewModel feedViewModel,
        ModelCatalogViewModel catalog,
        NetworkViewModel network,
        UnityProjectService unityProject,
        AppConfig config)
    {
        Graph = graph;
        _factory = factory;
        _serializer = serializer;
        _dialogs = dialogs;
        _feed = feed;
        _config = config;

        Feed = feedViewModel;
        Catalog = catalog;
        Network = network;
        UnityProject = unityProject;
        PendingConnection = new PendingConnectionViewModel(graph, message => _feed.Error("Connection refused", message));

        Graph.Nodes.CollectionChanged += OnNodesChanged;
        UnityProject.PropertyChanged += OnUnityProjectChanged;

        // The graph handed in is normally empty, but it does not have to be.
        ResyncNodeSubscriptions();
    }

    /// <summary>The document on the canvas.</summary>
    public GraphModel Graph { get; }

    /// <summary>The bottom panel.</summary>
    public ActivityFeedViewModel Feed { get; }

    /// <summary>Catalog commands used by the model node settings panel.</summary>
    public ModelCatalogViewModel Catalog { get; }

    /// <summary>The Network tab: available models, coverage, sources and contribution.</summary>
    public NetworkViewModel Network { get; }

    /// <summary>The Unity project that output nodes write into.</summary>
    public UnityProjectService UnityProject { get; }

    /// <summary>The wire currently being dragged.</summary>
    public PendingConnectionViewModel PendingConnection { get; }

    /// <summary>The node types offered by the palette.</summary>
    public IReadOnlyList<NodeFactory.NodeDescriptor> NodePalette => NodeFactory.Descriptors;

    /// <summary>True when a node is selected.</summary>
    public bool HasSelection => SelectedNode is not null;

    /// <summary>Text for the window title bar.</summary>
    public string WindowTitle
    {
        get
        {
            var graphName = CurrentGraphPath is null
                ? "Untitled graph"
                : Path.GetFileName(CurrentGraphPath);

            return $"LocalNEXUS  |  {graphName}";
        }
    }

    /// <summary>Adds a node of the given type at the cursor.</summary>
    [RelayCommand]
    private void AddNode(string? typeKey)
    {
        if (string.IsNullOrWhiteSpace(typeKey))
        {
            return;
        }

        try
        {
            var location = NextNodeLocation();
            var node = _factory.Create(typeKey, location.X, location.Y);
            Graph.AddNode(node);
            SelectOnly(node);
        }
        catch (NotSupportedException ex)
        {
            _dialogs.ShowError("Node not added", ex.Message);
        }
    }

    /// <summary>Removes every selected node together with its wires.</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelection()
    {
        foreach (var node in Graph.Nodes.Where(n => n.IsSelected).ToList())
        {
            Graph.RemoveNode(node);
        }

        SelectedNode = null;
    }

    /// <summary>Removes every wire attached to a pin. Invoked by the canvas when a connector is detached.</summary>
    [RelayCommand]
    private void DisconnectPin(Pin? pin)
    {
        if (pin is not null)
        {
            Graph.DisconnectPin(pin);
        }
    }

    /// <summary>Removes a single wire. Invoked by the canvas when a connection is cut.</summary>
    [RelayCommand]
    private void RemoveConnection(Connection? connection)
    {
        if (connection is not null)
        {
            Graph.RemoveConnection(connection);
        }
    }

    /// <summary>Opens a Unity project folder and remembers it for the next session.</summary>
    [RelayCommand]
    private void OpenUnityProject()
    {
        var folder = _dialogs.PickFolder("Choose a Unity project folder", _config.LastUnityProjectPath);
        if (folder is null)
        {
            return;
        }

        try
        {
            UnityProject.Open(folder);
            _config.LastUnityProjectPath = UnityProject.ProjectPath;
            _config.Save();

            _feed.Info(
                "Unity project opened",
                UnityProject.LooksLikeUnityProject
                    ? UnityProject.ProjectPath
                    : $"{UnityProject.ProjectPath} (no Assets folder found, output nodes will create one)");
        }
        catch (DirectoryNotFoundException ex)
        {
            _dialogs.ShowError("Project not opened", ex.Message);
        }
    }

    /// <summary>Clears the canvas.</summary>
    [RelayCommand]
    private void NewGraph()
    {
        Graph.Clear();
        SelectedNode = null;
        CurrentGraphPath = null;
        _cascadeIndex = 0;
        _feed.Info("New graph", "The canvas was cleared.");
    }

    /// <summary>Saves the graph, asking for a path.</summary>
    [RelayCommand]
    private void SaveGraph()
    {
        AppPaths.EnsureCreated();

        var suggested = CurrentGraphPath is null
            ? "graph" + GraphSerializer.FileExtension
            : Path.GetFileName(CurrentGraphPath);

        var path = _dialogs.PickSaveFile(
            "Save graph",
            suggested,
            $"LocalNEXUS graph (*{GraphSerializer.FileExtension})|*{GraphSerializer.FileExtension}|JSON (*.json)|*.json",
            Path.GetDirectoryName(CurrentGraphPath) ?? AppPaths.Graphs);

        if (path is null)
        {
            return;
        }

        try
        {
            _serializer.Save(Graph, path);
            CurrentGraphPath = path;
            _config.LastGraphPath = path;
            _config.Save();
            _feed.Info("Graph saved", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Graph not saved", ex.Message);
        }
    }

    /// <summary>Loads a graph, replacing whatever is on the canvas.</summary>
    [RelayCommand]
    private void LoadGraph()
    {
        AppPaths.EnsureCreated();

        var path = _dialogs.PickOpenFile(
            "Load graph",
            $"LocalNEXUS graph (*{GraphSerializer.FileExtension})|*{GraphSerializer.FileExtension}|JSON (*.json)|*.json|All files (*.*)|*.*",
            Path.GetDirectoryName(CurrentGraphPath) ?? AppPaths.Graphs);

        if (path is null)
        {
            return;
        }

        LoadGraphFrom(path);
    }

    /// <summary>Loads a graph from an explicit path. Used by the File menu and at startup.</summary>
    public void LoadGraphFrom(string path)
    {
        try
        {
            SelectedNode = null;
            var warnings = _serializer.LoadInto(Graph, path);

            CurrentGraphPath = path;
            _config.LastGraphPath = path;
            _config.Save();

            _feed.Info("Graph loaded", $"{Graph.Nodes.Count} nodes, {Graph.Connections.Count} connections from {path}");

            foreach (var warning in warnings)
            {
                _feed.Error("Graph load warning", warning);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Graph not loaded", ex.Message);
        }
    }

    /// <summary>
    /// Chooses where a newly added node lands. Nodify exposes the cursor position as a read only
    /// dependency property, which cannot be bound, so new nodes cascade from the top left instead
    /// and the user drags them where they want.
    /// </summary>
    private Point NextNodeLocation()
    {
        var step = _cascadeIndex++;
        var column = step / 8;
        var row = step % 8;

        return new Point(90d + (column * 260d) + (row * CascadeStep), 90d + (row * CascadeStep));
    }

    private void SelectOnly(NodeBase node)
    {
        foreach (var other in Graph.Nodes)
        {
            other.IsSelected = other == node;
        }

        SelectedNode = node;
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A reset carries no item lists, which is what clearing the canvas raises. Rebuilding the
        // subscription set from the collection covers that case as well as ordinary add and
        // remove, and stops nodes from a discarded graph holding this view model alive.
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResyncNodeSubscriptions();
            SelectedNode = null;
            return;
        }

        foreach (var node in e.OldItems?.OfType<NodeBase>() ?? Enumerable.Empty<NodeBase>())
        {
            Unobserve(node);
        }

        foreach (var node in e.NewItems?.OfType<NodeBase>() ?? Enumerable.Empty<NodeBase>())
        {
            Observe(node);
        }

        if (SelectedNode is not null && !Graph.Nodes.Contains(SelectedNode))
        {
            SelectedNode = null;
        }
    }

    private void ResyncNodeSubscriptions()
    {
        foreach (var node in _observedNodes.ToList())
        {
            Unobserve(node);
        }

        foreach (var node in Graph.Nodes)
        {
            Observe(node);
        }
    }

    private void Observe(NodeBase node)
    {
        if (_observedNodes.Add(node))
        {
            node.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void Unobserve(NodeBase node)
    {
        if (_observedNodes.Remove(node))
        {
            node.PropertyChanged -= OnNodePropertyChanged;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NodeBase.IsSelected))
        {
            SelectedNode = Graph.Nodes.LastOrDefault(n => n.IsSelected);
        }
    }

    private void OnUnityProjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UnityProjectService.StatusText))
        {
            OnPropertyChanged(nameof(WindowTitle));
        }
    }
}
