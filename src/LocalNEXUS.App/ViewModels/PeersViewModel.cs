using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The peer panel: known sources and their health, this machine's contribution, and the
/// coverage chain for the selected model node.
/// </summary>
public sealed partial class PeersViewModel : ObservableObject
{
    private readonly SourceRegistry _registry;
    private readonly RpcWorkerManager _worker;
    private readonly CoveragePlanner _planner;
    private readonly SourceHealthMonitor _monitor;
    private readonly IActivityFeed _feed;

    private ModelNode? _contextNode;

    /// <summary>Whether the panel is expanded. The strip toggle flips this.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPanelCollapsed))]
    private bool _isPanelVisible = true;

    /// <summary>Display name typed into the add source form.</summary>
    [ObservableProperty]
    private string _newSourceName = string.Empty;

    /// <summary>Host typed into the add source form.</summary>
    [ObservableProperty]
    private string _newSourceHost = string.Empty;

    /// <summary>Port typed into the add source form. rpc-server's default is 50052.</summary>
    [ObservableProperty]
    private string _newSourcePort = "50052";

    /// <summary>Declared memory typed into the add source form. Blank means unknown.</summary>
    [ObservableProperty]
    private string _newSourceMemoryMb = string.Empty;

    /// <summary>True when the new source is reached over the internet rather than the LAN.</summary>
    [ObservableProperty]
    private bool _newSourceIsInternet;

    /// <summary>The coverage preview for the selected model node, or null when there is nothing to preview.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCoveragePlan))]
    private CoveragePlan? _coveragePlan;

    /// <summary>One line explaining the preview, or why there is none.</summary>
    [ObservableProperty]
    private string _coverageStatus = "Select a Model node to preview coverage.";

    public PeersViewModel(
        SourceRegistry registry,
        RpcWorkerManager worker,
        CoveragePlanner planner,
        SourceHealthMonitor monitor,
        IActivityFeed feed)
    {
        _registry = registry;
        _worker = worker;
        _planner = planner;
        _monitor = monitor;
        _feed = feed;

        _registry.Changed += (_, _) => RecomputeCoverage();
    }

    /// <summary>The registry the sources list binds to directly.</summary>
    public SourceRegistry Registry => _registry;

    /// <summary>The contribution manager the serve controls bind to directly.</summary>
    public RpcWorkerManager Worker => _worker;

    /// <summary>True when there is a plan worth drawing.</summary>
    public bool HasCoveragePlan => CoveragePlan is not null;

    /// <summary>The inverse of <see cref="IsPanelVisible"/>, for the column collapse behaviour.</summary>
    public bool IsPanelCollapsed => !IsPanelVisible;

    /// <summary>
    /// Follows canvas selection. Called by the main view model whenever the selected node
    /// changes; a model node becomes the coverage preview's subject, anything else clears it.
    /// </summary>
    public void SetContext(NodeBase? node)
    {
        if (_contextNode is not null)
        {
            _contextNode.PropertyChanged -= OnContextNodeChanged;
        }

        _contextNode = node as ModelNode;

        if (_contextNode is not null)
        {
            _contextNode.PropertyChanged += OnContextNodeChanged;
        }

        RecomputeCoverage();
    }

    [RelayCommand]
    private void TogglePanel() => IsPanelVisible = !IsPanelVisible;

    /// <summary>Starts or stops serving this machine's compute to other orchestrators.</summary>
    [RelayCommand]
    private async Task ToggleContributionAsync()
    {
        try
        {
            if (_worker.IsRunning)
            {
                await _worker.StopAsync();
            }
            else
            {
                await _worker.StartAsync(CancellationToken.None);
            }
        }
        catch (ModelClientException ex)
        {
            _feed.Error("Contribution failed", ex.Message);
        }
    }

    /// <summary>Registers the source described by the add form and probes it straight away.</summary>
    [RelayCommand]
    private async Task AddSourceAsync()
    {
        if (!int.TryParse(NewSourcePort, out var port) || port is < 1 or > 65535)
        {
            _feed.Error("Source not added", "The port has to be a number between 1 and 65535.");
            return;
        }

        long memoryMb = 0;
        if (!string.IsNullOrWhiteSpace(NewSourceMemoryMb)
            && (!long.TryParse(NewSourceMemoryMb, out memoryMb) || memoryMb < 0))
        {
            _feed.Error("Source not added", "Declared memory has to be a number of MiB, or blank for unknown.");
            return;
        }

        var locality = NewSourceIsInternet ? SourceLocality.Remote : SourceLocality.LocalNetwork;
        var added = _registry.AddSource(NewSourceName, NewSourceHost, port, locality, memoryMb);

        if (added is null)
        {
            _feed.Error("Source not added", "The host is empty or that endpoint is already registered.");
            return;
        }

        NewSourceName = string.Empty;
        NewSourceHost = string.Empty;
        NewSourceMemoryMb = string.Empty;

        await _monitor.ProbeNowAsync(added, CancellationToken.None);
    }

    /// <summary>Removes a source. This machine cannot be removed and the button is hidden for it.</summary>
    [RelayCommand]
    private void RemoveSource(InferenceSource? source)
    {
        if (source is not null)
        {
            _registry.RemoveSource(source);
        }
    }

    /// <summary>Probes one source immediately, outside the ten second cadence.</summary>
    [RelayCommand]
    private async Task ProbeNowAsync(InferenceSource? source)
    {
        if (source is { IsThisMachine: false })
        {
            await _monitor.ProbeNowAsync(source, CancellationToken.None);
        }
    }

    private void OnContextNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ModelNode.Provider)
            or nameof(ModelNode.SelectedLocalModel)
            or nameof(ModelNode.ForceSplit)
            or nameof(ModelNode.SplitProportions)
            or nameof(ModelNode.BaseUrl))
        {
            RecomputeCoverage();
        }
    }

    /// <summary>
    /// Rebuilds the coverage preview with exactly the inputs a run would use, so what the
    /// panel shows is what the launch gate will decide.
    /// </summary>
    private void RecomputeCoverage()
    {
        var node = _contextNode;

        if (node is null)
        {
            CoveragePlan = null;
            CoverageStatus = "Select a Model node to preview coverage.";
            return;
        }

        if (node.Provider != ModelProvider.Local)
        {
            CoveragePlan = null;
            CoverageStatus = "Coverage applies to local models only.";
            return;
        }

        if (!string.IsNullOrWhiteSpace(node.BaseUrl))
        {
            CoveragePlan = null;
            CoverageStatus = "This node points at its own server; nothing is assembled.";
            return;
        }

        var modelPath = node.SelectedLocalModel?.Path;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            CoveragePlan = null;
            CoverageStatus = "No local model selected.";
            return;
        }

        GgufModelInfo metadata;
        try
        {
            metadata = GgufMetadata.Read(modelPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            CoveragePlan = null;
            CoverageStatus = $"Model metadata unreadable: {ex.Message}";
            return;
        }

        var plan = _planner.Plan(metadata, node.ForceSplit, ModelNode.ParseSplitProportions(node.SplitProportions));
        CoveragePlan = plan;

        if (!plan.IsComplete)
        {
            CoverageStatus = plan.IncompleteReason ?? "Coverage is incomplete.";
        }
        else if (plan.IsSplit)
        {
            CoverageStatus =
                $"Split across {plan.Assignments.Count} sources. Weakest section has {plan.WeakestAssignment.Redundancy} candidate(s).";
        }
        else
        {
            CoverageStatus = "Fits on this machine.";
        }
    }
}
