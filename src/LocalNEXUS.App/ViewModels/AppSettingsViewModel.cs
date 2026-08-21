using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.App.Services.Theming;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The settings panel: everything that is true of the application rather than of one node.
/// </summary>
/// <remarks>
/// The dividing line is ownership, not convenience. A watched model folder, a theme and a cloud
/// key are properties of this install, so they live here and every graph sees the same ones. Which
/// model a node uses and which file it writes are properties of that graph, so they stay on the
/// node where they can be saved with it and be different in the next graph.
///
/// The defaults section is the one place the line blurs, and it resolves the same way: what is
/// stored here is the value a newly added node starts from, never a value that reaches back into
/// nodes that already exist. Changing a default cannot silently change a graph somebody saved.
/// </remarks>
public sealed partial class AppSettingsViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly ModelCatalog _catalog;
    private readonly ProjectIndexService _index;
    private readonly IDialogService _dialogs;
    private readonly Func<Task> _reindex;

    /// <summary>Which section of the panel is showing.</summary>
    [ObservableProperty]
    private SettingsSection _section = SettingsSection.Appearance;

    public AppSettingsViewModel(
        AppConfig config,
        ThemeService themes,
        ModelCatalog catalog,
        ModelCatalogViewModel catalogCommands,
        PythonEnvironmentViewModel python,
        NetworkViewModel network,
        ExtensionsViewModel extensions,
        ProjectIndexService index,
        IDialogService dialogs,
        Func<Task> reindex)
    {
        _config = config;
        _catalog = catalog;
        Extensions = extensions;
        _index = index;
        _dialogs = dialogs;
        _reindex = reindex;

        Themes = themes;
        Catalog = catalogCommands;
        Python = python;
        Network = network;

        ThemeChoices = ThemeService.Available
            .Select(t => new ThemeChoiceViewModel(t, ApplyTheme, t.Theme == themes.Current))
            .ToList();

        RefreshEntries();
    }

    /// <summary>The theme picker binds to this directly, so choosing one applies it at once.</summary>
    public ThemeService Themes { get; }

    /// <summary>Catalogue commands, shared with the model node panel.</summary>
    public ModelCatalogViewModel Catalog { get; }

    /// <summary>The Python runtime, with its provisioning, healthy and broken states.</summary>
    public PythonEnvironmentViewModel Python { get; }

    /// <summary>Mesh membership and contribution.</summary>
    public NetworkViewModel Network { get; }

    /// <summary>The extensions registered against the open project.</summary>
    public ExtensionsViewModel Extensions { get; }

    /// <summary>What the project index currently knows.</summary>
    public ProjectIndexService Index => _index;

    /// <summary>
    /// Everything feeding the catalogue: the folders being scanned and the models added by name.
    /// </summary>
    public ObservableCollection<CatalogEntryViewModel> Entries { get; } = new();

    /// <summary>What the last add or rescan did, said in the panel rather than in a dialog.</summary>
    public string CatalogMessage
    {
        get => _catalogMessage;
        private set => SetProperty(ref _catalogMessage, value);
    }

    private string _catalogMessage = string.Empty;

    /// <summary>Every theme that can be picked, with the one in force marked.</summary>
    public IReadOnlyList<ThemeChoiceViewModel> ThemeChoices { get; }

    /// <summary>The sections of this panel, in the order they are listed.</summary>
    public IReadOnlyList<SettingsSection> Sections { get; } = Enum.GetValues<SettingsSection>();

    /// <summary>
    /// Applies a theme, at once and for the next session, and takes the mark off the others.
    /// </summary>
    private void ApplyTheme(ThemeChoiceViewModel choice)
    {
        Themes.Apply(choice.Definition.Theme);

        foreach (var candidate in ThemeChoices)
        {
            if (candidate != choice)
            {
                candidate.SetSelectedQuietly(false);
            }
        }
    }

    /// <summary>Where cloud requests go by default. Blank uses whatever the provider defaults to.</summary>
    public string CloudBaseUrl
    {
        get => _config.CloudBaseUrl ?? string.Empty;
        set => SetConfig(value, v => _config.CloudBaseUrl = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
    }

    /// <summary>The key a newly added model node starts with.</summary>
    public string CloudApiKey
    {
        get => _config.CloudApiKey ?? string.Empty;
        set => SetConfig(value, v => _config.CloudApiKey = string.IsNullOrWhiteSpace(v) ? null : v.Trim());
    }

    /// <summary>Repair attempts a newly added compile check node starts with.</summary>
    public int DefaultRetryLimit
    {
        get => _config.DefaultRetryLimit;
        set => SetConfig(Math.Clamp(value, 0, 10), v => _config.DefaultRetryLimit = v);
    }

    /// <summary>Characters of project map a newly added plan node starts with.</summary>
    public int DefaultMapCharacters
    {
        get => _config.DefaultMapCharacters;
        set => SetConfig(Math.Max(0, value), v => _config.DefaultMapCharacters = v, nameof(DefaultBudgetSummary));
    }

    /// <summary>Characters of candidate file contents a newly added plan node starts with.</summary>
    public int DefaultCandidateCharacters
    {
        get => _config.DefaultCandidateCharacters;
        set => SetConfig(Math.Max(0, value), v => _config.DefaultCandidateCharacters = v, nameof(DefaultBudgetSummary));
    }

    /// <summary>Characters of same-run signatures a newly added plan node starts with.</summary>
    public int DefaultEmittedCharacters
    {
        get => _config.DefaultEmittedCharacters;
        set => SetConfig(Math.Max(0, value), v => _config.DefaultEmittedCharacters = v, nameof(DefaultBudgetSummary));
    }

    /// <summary>How many candidate files a newly added plan node offers before reading any.</summary>
    public int DefaultCandidateLimit
    {
        get => _config.DefaultCandidateLimit;
        set => SetConfig(Math.Clamp(value, 1, 64), v => _config.DefaultCandidateLimit = v);
    }

    /// <summary>The three budgets as one sentence, in characters and in approximate tokens.</summary>
    public string DefaultBudgetSummary => new ContextBudget
    {
        MapCharacters = DefaultMapCharacters,
        CandidateCharacters = DefaultCandidateCharacters,
        EmittedSignatureCharacters = DefaultEmittedCharacters,
        CandidateLimit = DefaultCandidateLimit
    }.Summary;

    /// <summary>Adds a folder that will be searched, and keeps being searched.</summary>
    [RelayCommand]
    private void AddFolder()
    {
        var folder = _dialogs.PickFolder("Choose a folder to search for models", AppPaths.Models);

        if (folder is not null)
        {
            Report(_catalog.AddFolder(folder));
        }
    }

    /// <summary>
    /// Adds one model file, which is the path a folder picker could never offer.
    /// </summary>
    /// <remarks>
    /// This exists because picking a folder full of models used to be the only way in, and a
    /// folder picker lists folders, so the models themselves were invisible in it and the whole
    /// thing looked broken while working exactly as written.
    /// </remarks>
    [RelayCommand]
    private void AddModelFile()
    {
        var file = _dialogs.PickOpenFile(
            "Choose a model file",
            "Models (*.gguf;*.safetensors)|*.gguf;*.safetensors|All files (*.*)|*.*",
            AppPaths.Models);

        if (file is not null)
        {
            Report(_catalog.AddModel(file));
        }
    }

    /// <summary>
    /// Adds one safetensors model, which is a folder rather than a file, without registering
    /// everything that happens to sit beside it.
    /// </summary>
    [RelayCommand]
    private void AddModelFolder()
    {
        var folder = _dialogs.PickFolder("Choose a model folder", AppPaths.Models);

        if (folder is not null)
        {
            Report(_catalog.AddModel(folder));
        }
    }

    /// <summary>Drops a folder from the search set, or stops offering a model added by name.</summary>
    [RelayCommand]
    private void RemoveEntry(CatalogEntryViewModel? entry)
    {
        if (entry is null)
        {
            return;
        }

        var removed = entry.IsFolder
            ? _catalog.RemoveFolder(entry.Path)
            : _catalog.RemoveModel(entry.Path);

        if (removed)
        {
            CatalogMessage = entry.IsFolder
                ? $"No longer searching {entry.Path}."
                : $"No longer offering {entry.Label}.";
        }

        RefreshEntries();
    }

    /// <summary>Searches every folder again.</summary>
    [RelayCommand]
    private void Rescan()
    {
        _catalog.Refresh();
        RefreshEntries();

        CatalogMessage = _catalog.Models.Count == 1
            ? "1 model found."
            : $"{_catalog.Models.Count} models found.";
    }

    /// <summary>Opens the file that lists extra folders, one per line.</summary>
    [RelayCommand]
    private void EditModelPaths()
    {
        ModelPathsFile.EnsureCreated();
        _dialogs.OpenFileInEditor(AppPaths.ModelPathsFile);
    }

    /// <summary>Reads the open project again from scratch.</summary>
    [RelayCommand]
    private async Task ReindexAsync()
    {
        _index.Forget();
        await _reindex().ConfigureAwait(false);
    }

    /// <summary>Opens the folder this install keeps its configuration and logs in.</summary>
    [RelayCommand]
    private void OpenDataFolder()
    {
        AppPaths.EnsureCreated();
        _dialogs.OpenFolderInExplorer(AppPaths.Root);
    }

    /// <summary>Says what happened, and rebuilds the list when something changed.</summary>
    private void Report(CatalogAddition result)
    {
        CatalogMessage = result.Message;

        if (result.Added)
        {
            RefreshEntries();
        }
    }

    private void RefreshEntries()
    {
        Entries.Clear();

        foreach (var folder in _catalog.SearchFolders)
        {
            var removable = _catalog.IsRemovable(folder);

            var origin = removable
                ? "searched, added here"
                : string.Equals(Path.GetFullPath(folder), Path.GetFullPath(AppPaths.Models), StringComparison.OrdinalIgnoreCase)
                    ? "searched, built in"
                    : "searched, listed in model-paths.txt";

            Entries.Add(new CatalogEntryViewModel(folder, CatalogEntryKind.ScannedFolder, folder, origin, removable));
        }

        foreach (var path in _catalog.DirectPaths)
        {
            var model = _catalog.FindByPath(path);

            // A model added by name and then moved or deleted stays on the list saying so, rather
            // than disappearing and leaving somebody wondering where their entry went.
            var detail = model is null
                ? "added on its own, and no longer at that path"
                : $"added on its own, {model.Descriptor.SizeLabel}, {model.FormatLabel}";

            Entries.Add(new CatalogEntryViewModel(
                path,
                CatalogEntryKind.Model,
                model?.Name ?? Path.GetFileName(path),
                detail,
                CanRemove: true));
        }

        OnPropertyChanged(nameof(Catalog));
    }

    /// <summary>
    /// Writes a setting through to the file. Settings save as they are changed rather than behind
    /// an apply button, because a panel with no apply button cannot be left in a state where what
    /// is on screen and what is in force disagree.
    /// </summary>
    private void SetConfig<T>(T value, Action<T> assign, string? alsoChanged = null, [System.Runtime.CompilerServices.CallerMemberName] string? property = null)
    {
        assign(value);
        _config.Save();

        OnPropertyChanged(property);

        if (alsoChanged is not null)
        {
            OnPropertyChanged(alsoChanged);
        }
    }
}
