using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The commands a model node's settings panel needs in order to manage the model catalog.
/// </summary>
/// <remarks>
/// These belong here rather than on the node: adding a folder changes application configuration
/// and affects every model node, so it is not a per node setting.
/// </remarks>
public sealed partial class ModelCatalogViewModel : ObservableObject
{
    private readonly ModelCatalog _catalog;
    private readonly IDialogService _dialogs;

    public ModelCatalogViewModel(ModelCatalog catalog, IDialogService dialogs)
    {
        _catalog = catalog;
        _dialogs = dialogs;
    }

    /// <summary>Every discovered model, in either format.</summary>
    public ObservableCollection<LocalModelInfo> Models => _catalog.Models;

    /// <summary>True while a scan is in progress.</summary>
    public bool IsScanning => _catalog.IsScanning;

    /// <summary>Summary of where models are being looked for, shown under the dropdown.</summary>
    public string SearchSummary
    {
        get
        {
            var folders = _catalog.SearchFolders.Count();

            if (Models.Count == 0)
            {
                return $"No models found in {folders} folder(s). Add a folder or drop a model into {AppPaths.Models}";
            }

            // What was found but cannot be served is reported rather than silently dropped, so a
            // folder of weights with no config beside them does not read as an empty folder.
            var summary = $"{Models.Count} model(s) across {folders} folder(s)";

            return _catalog.UnservableCount == 0
                ? summary
                : $"{summary}. {_catalog.UnservableCount} other item(s) look like model files but cannot be served on their own.";
        }
    }

    /// <summary>Asks for a folder, adds it to the search set, and rescans.</summary>
    [RelayCommand]
    private void AddFolder()
    {
        var folder = _dialogs.PickFolder("Choose a folder containing models", AppPaths.Models);
        if (folder is null)
        {
            return;
        }

        if (!_catalog.AddFolder(folder))
        {
            _dialogs.ShowError("Folder not added", "That folder is already being scanned, or it no longer exists.");
        }

        NotifySummaryChanged();
    }

    /// <summary>Rescans every search folder.</summary>
    [RelayCommand]
    private void Refresh()
    {
        _catalog.Refresh();
        NotifySummaryChanged();
    }

    /// <summary>Opens the default models folder in Explorer so a model can be dropped in.</summary>
    [RelayCommand]
    private void OpenModelsFolder()
    {
        AppPaths.EnsureCreated();
        _dialogs.OpenFolderInExplorer(AppPaths.Models);
    }

    /// <summary>
    /// Opens the model paths file for editing. Adding a drive full of models is one line in one
    /// file, which is a smaller thing to explain than a dialog per folder.
    /// </summary>
    [RelayCommand]
    private void EditModelPaths()
    {
        ModelPathsFile.EnsureCreated();
        _dialogs.OpenFileInEditor(AppPaths.ModelPathsFile);
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(IsScanning));
    }
}
