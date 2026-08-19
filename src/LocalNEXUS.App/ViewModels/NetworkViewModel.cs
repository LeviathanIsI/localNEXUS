using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The Network tab: what the network can serve, the coverage chain of the selected model,
/// the known sources, and this machine's contribution.
/// </summary>
/// <remarks>
/// The models list leads and the sources are the underlying detail, because the question the
/// screen answers is "which models can the network serve", not "which machines do I know
/// about". Everything binds to the index and the registry directly; when discovery starts
/// feeding them, this view model does not change.
/// </remarks>
public sealed partial class NetworkViewModel : ObservableObject
{
    private readonly SourceRegistry _registry;
    private readonly RpcWorkerManager _worker;
    private readonly SourceHealthMonitor _monitor;
    private readonly IActivityFeed _feed;

    /// <summary>The model whose coverage chain is shown. Selecting a complete one arms it for use.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private NetworkServedModel? _selectedModel;

    /// <summary>Whether the add source form is open. It lives behind the plus, not on the page.</summary>
    [ObservableProperty]
    private bool _isAddSourceOpen;

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

    public NetworkViewModel(
        NetworkModelIndex index,
        SourceRegistry registry,
        RpcWorkerManager worker,
        SourceHealthMonitor monitor,
        IActivityFeed feed)
    {
        Index = index;
        _registry = registry;
        _worker = worker;
        _monitor = monitor;
        _feed = feed;

        Index.Models.CollectionChanged += OnModelsChanged;
        SelectedModel = Index.Models.FirstOrDefault();
    }

    /// <summary>The live list of models the network can serve. The primary surface.</summary>
    public NetworkModelIndex Index { get; }

    /// <summary>The known sources, this machine always included.</summary>
    public SourceRegistry Registry => _registry;

    /// <summary>The contribution manager the serve card binds to directly.</summary>
    public RpcWorkerManager Worker => _worker;

    /// <summary>True when a model is selected, which is when the chain has something to draw.</summary>
    public bool HasSelection => SelectedModel is not null;

    /// <summary>Recomputes the list against the current sources on demand.</summary>
    [RelayCommand]
    private void RefreshModels() => Index.Refresh();

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

    /// <summary>Opens or closes the add source form.</summary>
    [RelayCommand]
    private void ToggleAddSource() => IsAddSourceOpen = !IsAddSourceOpen;

    /// <summary>Registers the source described by the form and probes it straight away.</summary>
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
        IsAddSourceOpen = false;

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

    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Keep a sensible selection without ever stealing one the user made: pick the first
        // row when nothing is selected, and let go of a row that no longer exists.
        if (SelectedModel is not null && !Index.Models.Contains(SelectedModel))
        {
            SelectedModel = null;
        }

        SelectedModel ??= Index.Models.FirstOrDefault();
    }
}
