using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The questions asked once, the first time a project is opened.
/// </summary>
/// <remarks>
/// Once, and only once, which is the point of saving the answers. Anybody who wants to change one
/// later finds all of it in Settings under this project, and the window says so on the way out.
///
/// Skippable, and nothing waits on it. Skipping takes the defaults and records that the asking has
/// happened, so it is not asked again; a window that reappeared until it was filled in would be a
/// window people learn to dismiss without reading.
/// </remarks>
public sealed partial class ProjectSetupViewModel : ObservableObject
{
    private readonly ProjectSettingsService _settings;
    private readonly System.Collections.ObjectModel.ObservableCollection<LocalModelInfo> _models;

    /// <summary>True while the window is up.</summary>
    [ObservableProperty]
    private bool _isOpen;

    /// <summary>Where generated code goes, chosen or typed.</summary>
    [ObservableProperty]
    private string _scriptsFolder = string.Empty;

    /// <summary>What the project is, detected and overridable.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KindNote))]
    private ProjectKind _kind = ProjectKind.Plain;

    /// <summary>The model a new Model node reaches for.</summary>
    [ObservableProperty]
    private LocalModelInfo? _defaultModel;

    /// <summary>Whether tool calls are answered while this project is open.</summary>
    [ObservableProperty]
    private bool _mcpServerEnabled;

    /// <summary>Whether the conventions are committed rather than ignored.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SharingNote))]
    private bool _shareSettings;

    /// <summary>What the project is called, for the heading.</summary>
    [ObservableProperty]
    private string _projectName = string.Empty;

    private ProjectKind _detected = ProjectKind.Plain;

    public ProjectSetupViewModel(
        ProjectSettingsService settings,
        System.Collections.ObjectModel.ObservableCollection<LocalModelInfo> models)
    {
        _settings = settings;
        _models = models;
    }

    /// <summary>Folders the project actually has, plus the default for its kind.</summary>
    public ObservableCollection<string> FolderChoices { get; } = new();

    /// <summary>Every model on this machine, for the one this project reaches for.</summary>
    public ObservableCollection<LocalModelInfo> Models { get; } = new();

    /// <summary>Both kinds, for the override.</summary>
    public IReadOnlyList<ProjectKind> Kinds { get; } = new[] { ProjectKind.Unity, ProjectKind.Plain };

    /// <summary>What detection made of it, and what changing it means.</summary>
    public string KindNote => Kind == _detected
        ? $"Detected as a {Describe(Kind)}."
        : $"Detected as a {Describe(_detected)}, overridden to {Describe(Kind)}. "
          + (Kind == ProjectKind.Unity
              ? "The Unity write rules will be applied to this project."
              : "The Unity write rules will not be applied, so a rename that would break a scene will not be refused.");

    /// <summary>Where the answers will be written, and which of them.</summary>
    public string SharingNote => ShareSettings
        ? $"{ProjectSettings.SharedFileName} will hold the folder and the project kind, for everybody working on this "
          + $"project. Your model choice and the tool call switch stay in {ProjectSettings.LocalFileName}, which is "
          + "never committed."
        : $"Everything goes in {ProjectSettings.LocalFileName}, and it is added to .gitignore if this project has one. "
          + "Turn this on to share the folder and the project kind with the rest of your team.";

    /// <summary>Opens the window for a project that has just been opened for the first time.</summary>
    public void Open(string projectName, ProjectKind detected)
    {
        ProjectName = projectName;
        _detected = detected == ProjectKind.None ? ProjectKind.Plain : detected;

        Kind = _settings.Kind == ProjectKind.None ? _detected : _settings.Kind;
        ScriptsFolder = _settings.ScriptsFolder;
        McpServerEnabled = _settings.McpServerEnabled;
        ShareSettings = _settings.ShareSettings;

        FolderChoices.Clear();

        foreach (var folder in _settings.ExistingFolders())
        {
            FolderChoices.Add(folder);
        }

        // The default for the kind, offered whether or not it is there yet, because a project that
        // has not got one is exactly the project that is about to.
        var suggested = ProjectSettingsService.DefaultFolderFor(Kind);

        if (!FolderChoices.Contains(suggested, StringComparer.OrdinalIgnoreCase))
        {
            FolderChoices.Insert(0, suggested);
        }

        if (ScriptsFolder.Length == 0)
        {
            ScriptsFolder = suggested;
        }

        Models.Clear();

        foreach (var model in _models)
        {
            Models.Add(model);
        }

        DefaultModel = Models.FirstOrDefault(m =>
            string.Equals(m.Path, _settings.DefaultModelPath, StringComparison.OrdinalIgnoreCase));

        IsOpen = true;
    }

    /// <summary>Takes the answers and remembers them.</summary>
    [RelayCommand]
    private void Save()
    {
        _settings.ScriptsFolder = ScriptsFolder.Trim().Replace('\\', '/').Trim('/');
        _settings.Kind = Kind;
        _settings.DefaultModelPath = DefaultModel?.Path ?? string.Empty;
        _settings.McpServerEnabled = McpServerEnabled;
        _settings.ShareSettings = ShareSettings;
        _settings.HasBeenSetUp = true;

        _settings.Save();

        IsOpen = false;
    }

    /// <summary>
    /// Closes without answering, keeping the defaults.
    /// </summary>
    /// <remarks>
    /// Records that the asking has happened all the same, so this is not asked twice. Somebody who
    /// skipped it has decided the defaults are fine, and asking again on the next open would be
    /// disagreeing with them.
    /// </remarks>
    [RelayCommand]
    private void Skip()
    {
        _settings.ScriptsFolder = ProjectSettingsService.DefaultFolderFor(_detected);
        _settings.Kind = _detected;
        _settings.HasBeenSetUp = true;

        _settings.Save();

        IsOpen = false;
    }

    partial void OnKindChanged(ProjectKind value)
    {
        // The suggestion follows the kind, because the whole reason the kind matters here is that
        // it decides where code goes.
        var suggested = ProjectSettingsService.DefaultFolderFor(value);

        if (!FolderChoices.Contains(suggested, StringComparer.OrdinalIgnoreCase))
        {
            FolderChoices.Insert(0, suggested);
        }
    }

    private static string Describe(ProjectKind kind)
        => kind == ProjectKind.Unity ? "Unity project" : "C# project";
}
