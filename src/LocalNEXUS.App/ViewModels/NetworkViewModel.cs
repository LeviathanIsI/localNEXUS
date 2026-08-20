using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.ViewModels.Network;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The Network section: what the mesh can serve, as a table of models, filtered from the sidebar.
/// </summary>
/// <remarks>
/// Models lead and machines are the detail underneath, because the question the screen answers is
/// "what can the network serve", not "which machines do I know about". A machine is a filter and
/// an inspector target rather than the spine of the page.
///
/// Everything binds to the mesh manager directly, so what is drawn is what the engine reports
/// rather than anything this view model computes. Where a column has no answer it says so: the
/// mesh reports coverage, peers and metadata, and does not report file size or throughput, and a
/// dash is the honest rendering of that.
///
/// Membership and contribution are launch settings of the node process, so changing one saves it
/// and restarts the node. That is deliberate: a half applied membership change would be a worse
/// surprise than a visible restart.
/// </remarks>
public sealed partial class NetworkViewModel : ObservableObject, IDisposable
{
    private readonly AppConfig _config;
    private readonly IActivityFeed _feed;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<NetworkServedModel, NetworkModelRow> _rows = new();

    private bool _disposed;

    /// <summary>The model whose coverage the inspector shows. Selecting a complete one arms it for use.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(InspectorTarget))]
    private NetworkServedModel? _selectedModel;

    /// <summary>The row backing <see cref="SelectedModel"/>, which is what the table highlights.</summary>
    [ObservableProperty]
    private NetworkModelRow? _selectedRow;

    /// <summary>The machine the sidebar has selected, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorTarget))]
    private InferenceSource? _selectedSource;

    /// <summary>The coverage section the inspector is showing, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InspectorTarget))]
    private SourceAssignment? _selectedSection;

    /// <summary>Free text typed into the filter box in the title bar.</summary>
    [ObservableProperty]
    private string _filterText = string.Empty;

    /// <summary>The column the table is ordered by.</summary>
    [ObservableProperty]
    private ModelColumn _sortColumn = ModelColumn.Coverage;

    /// <summary>True when the order is reversed.</summary>
    [ObservableProperty]
    private bool _sortDescending;

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

    /// <summary>Port the node's OpenAI compatible API listens on.</summary>
    [ObservableProperty]
    private string _apiPort;

    /// <summary>Advertises this mesh publicly. Off by default and the only setting that leaves the local network.</summary>
    [ObservableProperty]
    private bool _publish;

    public NetworkViewModel(MeshManager mesh, ModelCatalog catalog, AppConfig config, IActivityFeed feed, IDialogService dialogs)
    {
        Mesh = mesh;
        Catalog = catalog;
        _config = config;
        _feed = feed;
        _dialogs = dialogs;

        _meshName = string.IsNullOrWhiteSpace(config.MeshName) ? "LocalNEXUS" : config.MeshName;
        _contribute = config.MeshContribute;
        _publish = config.MeshPublish;
        _joinToken = config.MeshJoinToken ?? string.Empty;
        _apiPort = config.MeshApiPort.ToString(CultureInfo.InvariantCulture);
        _maxVramGb = config.MeshMaxVramGb > 0
            ? config.MeshMaxVramGb.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;
        _offeredModel = catalog.FindByPath(config.MeshOfferedModelPath) ?? catalog.Models.FirstOrDefault();

        Groups = BuildFilterGroups();

        Mesh.Models.CollectionChanged += OnModelsChanged;
        Mesh.Sources.CollectionChanged += OnSourcesChanged;
        Mesh.PropertyChanged += OnMeshChanged;

        RebuildRows();
        SelectedModel = Mesh.Models.FirstOrDefault();
    }

    /// <summary>This install's mesh node and everything it reports. The primary surface.</summary>
    public MeshManager Mesh { get; }

    /// <summary>The local model files, which is what this machine can offer to serve.</summary>
    public ModelCatalog Catalog { get; }

    /// <summary>Every model the mesh knows about, as table rows.</summary>
    public ObservableCollection<NetworkModelRow> Rows { get; } = new();

    /// <summary>The rows the filters and the sort leave, which is what the table draws.</summary>
    public ObservableCollection<NetworkModelRow> VisibleRows { get; } = new();

    /// <summary>The filter headings in the sidebar, above the contribute card.</summary>
    public IReadOnlyList<ModelFilterGroup> Groups { get; }

    /// <summary>The machines in the mesh, which are both a filter and something to inspect.</summary>
    public ObservableCollection<InferenceSource> Machines { get; } = new();

    /// <summary>True when a model is selected, which is when the coverage table has something to draw.</summary>
    public bool HasSelection => SelectedModel is not null;

    /// <summary>
    /// What the one inspector slot shows on this section. A section beats a machine and a machine
    /// beats a model, because that is the order of how specific the question is: someone who
    /// clicked an uncovered section is asking about that section.
    /// </summary>
    public object? InspectorTarget => (object?)SelectedSection ?? (object?)SelectedSource ?? SelectedModel;

    /// <summary>The right hand end of the status bar while this section is showing.</summary>
    public string CoverageSummary
    {
        get
        {
            if (!Mesh.IsRunning)
            {
                return "mesh node stopped";
            }

            var blocked = Rows.Count(r => r.Availability == ModelAvailability.Blocked);
            var starting = Rows.Count(r => r.Availability == ModelAvailability.Starting);

            if (blocked > 0)
            {
                return starting > 0
                    ? $"{blocked} blocked, {starting} starting"
                    : $"{blocked} blocked";
            }

            return starting > 0 ? $"{starting} starting" : $"{Rows.Count} model(s) complete";
        }
    }

    /// <summary>The invite token, which is the only way into a private mesh.</summary>
    public string InviteToken => Mesh.InviteToken;

    /// <summary>Orders the table by a column, reversing it when the same column is picked twice.</summary>
    [RelayCommand]
    private void Sort(ModelColumn column)
    {
        if (SortColumn == column)
        {
            SortDescending = !SortDescending;
        }
        else
        {
            SortColumn = column;
            SortDescending = false;
        }

        ApplyFilters();
    }

    /// <summary>Puts one filter in force within its group.</summary>
    [RelayCommand]
    private void ApplyFilter(ModelFilter? filter)
    {
        if (filter is null)
        {
            return;
        }

        foreach (var group in Groups.Where(g => g.Filters.Contains(filter)))
        {
            group.Select(filter);
        }

        ApplyFilters();
    }

    /// <summary>Clears whatever the inspector is pinned to, back to the selected model.</summary>
    [RelayCommand]
    private void ClearInspector()
    {
        SelectedSection = null;
        SelectedSource = null;
    }

    /// <summary>Puts the invite token on the clipboard, which is how another machine joins.</summary>
    [RelayCommand]
    private void CopyInvite()
    {
        if (string.IsNullOrWhiteSpace(Mesh.InviteToken))
        {
            _feed.Error("Nothing to copy", "The mesh node has not issued an invite token yet.");
            return;
        }

        _dialogs.CopyToClipboard(Mesh.InviteToken);
        _feed.Info("Invite token copied", "It is private and only usable on the local network.");
    }

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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Mesh.Models.CollectionChanged -= OnModelsChanged;
        Mesh.Sources.CollectionChanged -= OnSourcesChanged;
        Mesh.PropertyChanged -= OnMeshChanged;

        foreach (var row in _rows.Values)
        {
            row.PropertyChanged -= OnRowChanged;
            row.Dispose();
        }

        _rows.Clear();
    }

    partial void OnFilterTextChanged(string value) => ApplyFilters();

    partial void OnContributeChanged(bool value) => OnPropertyChanged(nameof(CoverageSummary));

    /// <summary>
    /// Picking a row in the table is picking a model, and it takes the inspector off whatever it
    /// was pinned to. Selection lives on the lists rather than behind commands so that the
    /// keyboard works in them for free.
    /// </summary>
    partial void OnSelectedRowChanged(NetworkModelRow? value)
    {
        SelectedSection = null;
        SelectedSource = null;
        SelectedModel = value?.Model;
    }

    partial void OnSelectedSourceChanged(InferenceSource? value)
    {
        if (value is not null)
        {
            SelectedSection = null;
        }
    }

    /// <summary>
    /// The filter groups. Two of them infer their answer from what the engine does report rather
    /// than being told it directly, and each says so in its note rather than pretending otherwise.
    /// </summary>
    private IReadOnlyList<ModelFilterGroup> BuildFilterGroups() => new[]
    {
        new ModelFilterGroup(
            "Status",
            "Whether the mesh can assemble the model right now.",
            new[]
            {
                new ModelFilter("All", _ => true, ApplyFilterCommand, isSelected: true),
                new ModelFilter("Complete", r => r.Availability == ModelAvailability.Complete, ApplyFilterCommand),
                new ModelFilter("Starting", r => r.Availability == ModelAvailability.Starting, ApplyFilterCommand),
                new ModelFilter("Blocked", r => r.Availability == ModelAvailability.Blocked, ApplyFilterCommand)
            }),

        new ModelFilterGroup(
            "Format",
            "Inferred from the quantization label, because the mesh reports a quantization and not a format. "
            + "A label a GGUF file would carry counts as GGUF; anything else is left unknown rather than guessed.",
            new[]
            {
                new ModelFilter("All", _ => true, ApplyFilterCommand, isSelected: true),
                new ModelFilter("GGUF", r => r.LooksLikeGguf, ApplyFilterCommand),
                new ModelFilter("Not reported", r => !r.LooksLikeGguf, ApplyFilterCommand)
            }),

        new ModelFilterGroup(
            "Provider",
            "Where the model is served from. Cloud models are configured on a model node and are not "
            + "part of the mesh, so this list never has any.",
            new[]
            {
                new ModelFilter("All", _ => true, ApplyFilterCommand, isSelected: true),
                new ModelFilter("Mesh", _ => true, ApplyFilterCommand),
                new ModelFilter("Cloud", _ => false, ApplyFilterCommand)
            }),

        new ModelFilterGroup(
            "Sharing",
            "Read from the posture of the mesh itself. A private mesh is joined by invitation, so "
            + "everything in it is invite only; publishing the mesh makes all of it public at once.",
            new[]
            {
                new ModelFilter("All", _ => true, ApplyFilterCommand, isSelected: true),
                new ModelFilter("Public", r => r.Sharing == ModelSharing.Public, ApplyFilterCommand),
                new ModelFilter("Invite only", r => r.Sharing == ModelSharing.InviteOnly, ApplyFilterCommand)
            })
    };

    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildRows();

        // Keep a sensible selection without ever stealing one the user made: pick the first row
        // when nothing is selected, and let go of a row that no longer exists.
        if (SelectedModel is not null && !Mesh.Models.Contains(SelectedModel))
        {
            SelectedModel = null;
            SelectedRow = null;
            SelectedSection = null;
        }

        if (SelectedModel is null)
        {
            SelectedModel = Mesh.Models.FirstOrDefault();
            SelectedRow = SelectedModel is null ? null : _rows.GetValueOrDefault(SelectedModel);
        }
    }

    private void OnSourcesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Machines.Clear();

        foreach (var source in Mesh.Sources)
        {
            Machines.Add(source);
        }

        if (SelectedSource is not null && !Mesh.Sources.Contains(SelectedSource))
        {
            SelectedSource = null;
        }

        OnPropertyChanged(nameof(CoverageSummary));
    }

    private void OnMeshChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MeshManager.IsPublic):
                foreach (var row in _rows.Values)
                {
                    row.RefreshMeshState();
                }

                ApplyFilters();
                break;

            case nameof(MeshManager.InviteToken):
                OnPropertyChanged(nameof(InviteToken));
                break;

            case nameof(MeshManager.State):
                OnPropertyChanged(nameof(CoverageSummary));
                break;
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A row republishes everything when the engine updates it, so the counts and the ordering
        // are recomputed rather than guessed at from which property changed.
        RefreshCounts();
        OnPropertyChanged(nameof(CoverageSummary));
    }

    private void RebuildRows()
    {
        var wanted = Mesh.Models.ToList();

        foreach (var gone in _rows.Keys.Except(wanted).ToList())
        {
            _rows[gone].PropertyChanged -= OnRowChanged;
            _rows[gone].Dispose();
            _rows.Remove(gone);
        }

        Rows.Clear();

        foreach (var model in wanted)
        {
            if (!_rows.TryGetValue(model, out var row))
            {
                row = new NetworkModelRow(model, () => Mesh.IsPublic);
                row.PropertyChanged += OnRowChanged;
                _rows[model] = row;
            }

            Rows.Add(row);
        }

        ApplyFilters();
        OnPropertyChanged(nameof(CoverageSummary));
    }

    private void RefreshCounts()
    {
        foreach (var group in Groups)
        {
            foreach (var filter in group.Filters)
            {
                filter.Count = Rows.Count(filter.Keeps);
            }
        }
    }

    private void ApplyFilters()
    {
        RefreshCounts();

        var text = FilterText.Trim();

        IEnumerable<NetworkModelRow> kept = Rows.Where(row =>
            Groups.All(group => group.Keeps(row))
            && (text.Length == 0
                || row.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || row.Quantisation.Contains(text, StringComparison.OrdinalIgnoreCase)));

        kept = SortDescending
            ? kept.OrderByDescending(r => r.SortKey(SortColumn))
            : kept.OrderBy(r => r.SortKey(SortColumn));

        VisibleRows.Clear();

        foreach (var row in kept)
        {
            VisibleRows.Add(row);
        }
    }

    private void SaveSettings()
    {
        _config.MeshContribute = Contribute;
        _config.MeshPublish = Publish;
        _config.MeshName = string.IsNullOrWhiteSpace(MeshName) ? "LocalNEXUS" : MeshName.Trim();
        _config.MeshJoinToken = string.IsNullOrWhiteSpace(JoinToken) ? null : JoinToken.Trim();
        _config.MeshOfferedModelPath = OfferedModel?.Path;
        _config.MeshMaxVramGb = ParseMaxVram(MaxVramGb);
        _config.MeshApiPort = ParsePort(ApiPort, _config.MeshApiPort);
        _config.Save();
    }

    /// <summary>
    /// Reads the memory cap. Unlike the previous engine's declared offer, which orchestrators
    /// merely honoured, this value is enforced by the mesh planner, so a bad one is worth refusing
    /// rather than silently treating as unlimited.
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

    private int ParsePort(string text, int fallback)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed is > 0 and < 65536)
        {
            return parsed;
        }

        _feed.Error("Port ignored", $"The port has to be a number between 1 and 65535. Keeping {fallback}.");
        return fallback;
    }
}
