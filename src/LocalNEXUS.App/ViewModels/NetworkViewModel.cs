using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The Network tab: what the network can serve, the coverage chain of the selected model,
/// the sources in the mesh, and this machine's contribution.
/// </summary>
/// <remarks>
/// The models list leads and the sources are the underlying detail, because the question the
/// screen answers is "which models can the network serve", not "which machines do I know
/// about". Everything binds to the mesh manager directly, so what is drawn is what the engine
/// reports rather than anything this view model computes.
///
/// Membership and contribution are launch settings of the node process, so changing one saves
/// it and restarts the node. That is deliberate: a half applied membership change would be a
/// worse surprise than a visible restart.
/// </remarks>
public sealed partial class NetworkViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly IActivityFeed _feed;

    /// <summary>The model whose coverage chain is shown. Selecting a complete one arms it for use.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private NetworkServedModel? _selectedModel;

    /// <summary>Whether the join form is open. It lives behind the plus, not on the page.</summary>
    [ObservableProperty]
    private bool _isJoinOpen;

    /// <summary>Invite token typed into the join form.</summary>
    [ObservableProperty]
    private string _joinToken = string.Empty;

    /// <summary>Name this install gives the mesh it hosts.</summary>
    [ObservableProperty]
    private string _meshName;

    /// <summary>Whether this machine offers its own compute rather than only routing.</summary>
    [ObservableProperty]
    private bool _contribute;

    /// <summary>The GGUF this machine serves while contributing.</summary>
    [ObservableProperty]
    private LocalModelInfo? _offeredModel;

    /// <summary>Cap on the memory offered, in GB. Blank lets the engine decide.</summary>
    [ObservableProperty]
    private string _maxVramGb;

    /// <summary>Advertises this mesh publicly. Off by default and the only setting that leaves the local network.</summary>
    [ObservableProperty]
    private bool _publish;

    public NetworkViewModel(MeshManager mesh, ModelCatalog catalog, AppConfig config, IActivityFeed feed)
    {
        Mesh = mesh;
        Catalog = catalog;
        _config = config;
        _feed = feed;

        _meshName = string.IsNullOrWhiteSpace(config.MeshName) ? "LocalNEXUS" : config.MeshName;
        _contribute = config.MeshContribute;
        _publish = config.MeshPublish;
        _joinToken = config.MeshJoinToken ?? string.Empty;
        _maxVramGb = config.MeshMaxVramGb > 0
            ? config.MeshMaxVramGb.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;
        _offeredModel = catalog.FindByPath(config.MeshOfferedModelPath) ?? catalog.Models.FirstOrDefault();

        Mesh.Models.CollectionChanged += OnModelsChanged;
        SelectedModel = Mesh.Models.FirstOrDefault();
    }

    /// <summary>This install's mesh node and everything it reports. The primary surface.</summary>
    public MeshManager Mesh { get; }

    /// <summary>The local GGUF files, which is what this machine can offer to serve.</summary>
    public ModelCatalog Catalog { get; }

    /// <summary>True when a model is selected, which is when the chain has something to draw.</summary>
    public bool HasSelection => SelectedModel is not null;

    /// <summary>Starts or stops this install's mesh node.</summary>
    [RelayCommand]
    private async Task ToggleMeshAsync()
    {
        try
        {
            if (Mesh.IsRunning)
            {
                await Mesh.StopAsync();
            }
            else
            {
                SaveSettings();
                await Mesh.StartAsync(CancellationToken.None);
            }
        }
        catch (ModelClientException ex)
        {
            _feed.Error("Mesh node failed", ex.Message);
        }
    }

    /// <summary>Applies the contribution and membership settings, restarting the node if it is up.</summary>
    [RelayCommand]
    private async Task ApplySettingsAsync()
    {
        SaveSettings();

        if (!Mesh.IsRunning)
        {
            return;
        }

        try
        {
            await Mesh.StopAsync();
            await Mesh.StartAsync(CancellationToken.None);
        }
        catch (ModelClientException ex)
        {
            _feed.Error("Mesh node failed", ex.Message);
        }
    }

    /// <summary>Opens or closes the join form.</summary>
    [RelayCommand]
    private void ToggleJoin() => IsJoinOpen = !IsJoinOpen;

    /// <summary>Joins the mesh the pasted invite token describes.</summary>
    [RelayCommand]
    private async Task JoinMeshAsync()
    {
        if (string.IsNullOrWhiteSpace(JoinToken))
        {
            _feed.Error("Mesh not joined", "Paste the invite token printed by the machine hosting the mesh.");
            return;
        }

        IsJoinOpen = false;
        await ApplySettingsAsync();
    }

    /// <summary>Leaves the joined mesh and goes back to hosting a private one.</summary>
    [RelayCommand]
    private async Task LeaveMeshAsync()
    {
        JoinToken = string.Empty;
        await ApplySettingsAsync();
    }

    private void SaveSettings()
    {
        _config.MeshContribute = Contribute;
        _config.MeshPublish = Publish;
        _config.MeshName = string.IsNullOrWhiteSpace(MeshName) ? "LocalNEXUS" : MeshName.Trim();
        _config.MeshJoinToken = string.IsNullOrWhiteSpace(JoinToken) ? null : JoinToken.Trim();
        _config.MeshOfferedModelPath = OfferedModel?.Path;
        _config.MeshMaxVramGb = ParseMaxVram(MaxVramGb);
        _config.Save();
    }

    /// <summary>
    /// Reads the memory cap. Unlike the previous engine's declared offer, which orchestrators
    /// merely honoured, this value is enforced by the mesh planner, so a bad one is worth
    /// refusing rather than silently treating as unlimited.
    /// </summary>
    private double ParseMaxVram(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0d;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
        {
            return parsed;
        }

        _feed.Error("Memory cap ignored", "The memory cap has to be a number of GB, or blank to let the engine decide.");
        return 0d;
    }

    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Keep a sensible selection without ever stealing one the user made: pick the first
        // row when nothing is selected, and let go of a row that no longer exists.
        if (SelectedModel is not null && !Mesh.Models.Contains(SelectedModel))
        {
            SelectedModel = null;
        }

        SelectedModel ??= Mesh.Models.FirstOrDefault();
    }
}
