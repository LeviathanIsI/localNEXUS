using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The commands a model node's settings panel needs in order to manage the GGUF catalog.
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

    /// <summary>Every discovered GGUF file.</summary>
    public ObservableCollection<LocalModelInfo> Models => _catalog.Models;

    /// <summary>True while a scan is in progress.</summary>
    public bool IsScanning => _catalog.IsScanning;

    /// <summary>Summary of where models are being looked for, shown under the dropdown.</summary>
    public string SearchSummary
    {
        get
        {
            var folders = _catalog.SearchFolders.Count();
            return Models.Count == 0
                ? $"No GGUF files found in {folders} folder(s). Add a folder or drop a model into {AppPaths.Models}"
                : $"{Models.Count} model(s) across {folders} folder(s)";
        }
    }

    /// <summary>Asks for a folder, adds it to the search set, and rescans.</summary>
    [RelayCommand]
    private void AddFolder()
    {
        var folder = _dialogs.PickFolder("Choose a folder containing GGUF models", AppPaths.Models);
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

    /// <summary>Opens the default models folder in Explorer so a GGUF can be dropped in.</summary>
    [RelayCommand]
    private void OpenModelsFolder()
    {
        AppPaths.EnsureCreated();
        _dialogs.OpenFolderInExplorer(AppPaths.Models);
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(IsScanning));
    }
}
