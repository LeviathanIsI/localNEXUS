using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// Discovers the GGUF files available for local inference and exposes them to model nodes.
/// </summary>
/// <remarks>
/// The default folder under the user data directory is always scanned, plus any extra folders
/// the user has added. A machine with no models is a normal state, not an error: the dropdown
/// is simply empty and the model node reports the problem when it is run.
/// </remarks>
public sealed partial class ModelCatalog : ObservableObject
{
    private readonly AppConfig _config;

    /// <summary>Set while a scan is in progress so the UI can show that it is working.</summary>
    [ObservableProperty]
    private bool _isScanning;

    public ModelCatalog(AppConfig config)
    {
        _config = config;
        Models = new ObservableCollection<LocalModelInfo>();
    }

    /// <summary>Every GGUF file found by the last scan, sorted by name.</summary>
    public ObservableCollection<LocalModelInfo> Models { get; }

    /// <summary>The folders currently being scanned, in the order they are searched.</summary>
    public IEnumerable<string> SearchFolders
        => new[] { AppPaths.Models }.Concat(_config.ExtraModelFolders).Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rescans every search folder and replaces the contents of <see cref="Models"/>.</summary>
    public void Refresh()
    {
        IsScanning = true;
        try
        {
            var found = new List<LocalModelInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in SearchFolders)
            {
                foreach (var file in EnumerateGgufFiles(folder))
                {
                    if (seen.Add(Path.GetFullPath(file)))
                    {
                        found.Add(new LocalModelInfo(file));
                    }
                }
            }

            Models.Clear();
            foreach (var model in found.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                Models.Add(model);
            }
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>
    /// Adds a folder to the search set, persists it, and rescans. Returns false when the folder
    /// is missing or already present.
    /// </summary>
    public bool AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        var full = Path.GetFullPath(folder);
        if (SearchFolders.Any(f => string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        _config.ExtraModelFolders.Add(full);
        _config.Save();
        Refresh();
        return true;
    }

    /// <summary>Finds the catalog entry for a path, or null when the file is no longer present.</summary>
    public LocalModelInfo? FindByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Models.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> EnumerateGgufFiles(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return Array.Empty<string>();
        }

        try
        {
            // Models are commonly stored one folder per model, so the scan recurses.
            return Directory.EnumerateFiles(folder, "*.gguf", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 4
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
