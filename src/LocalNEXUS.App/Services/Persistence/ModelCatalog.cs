using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// Discovers the models available for local inference and exposes them to model nodes.
/// </summary>
/// <remarks>
/// Two ways in, and they mean different things. A folder is a standing instruction to look inside
/// it and keep looking, and the default folder under the user data directory is always one of
/// them. A single model is one file or one safetensors folder, added by name, registering nothing
/// around it. Both end up in the same list and are indistinguishable once they are in it.
///
/// A machine with no models is a normal state, not an error: the dropdown is simply empty and the
/// model node reports the problem when it is run.
///
/// Both formats appear in one list. No filter gates it and nobody is asked to choose an engine,
/// because the format of a model is a fact about the file rather than a decision the user has to
/// make. What each entry is, GGUF or safetensors, is decided by looking inside it.
/// </remarks>
public sealed partial class ModelCatalog : ObservableObject
{
    private readonly AppConfig _config;

    /// <summary>Set while a scan is in progress so the UI can show that it is working.</summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>Models found by the last scan that cannot be served, and why.</summary>
    [ObservableProperty]
    private int _unservableCount;

    /// <summary>
    /// True when a scan stopped at the folder budget rather than because it ran out of folders.
    /// </summary>
    /// <remarks>
    /// Surfaced because the whole point of removing the depth limit was that a scan which quietly
    /// gave up is indistinguishable from a machine with no models in it.
    /// </remarks>
    [ObservableProperty]
    private bool _scanWasTruncated;

    public ModelCatalog(AppConfig config)
    {
        _config = config;
        Models = new ObservableCollection<LocalModelInfo>();
    }

    /// <summary>Every servable model found by the last scan, sorted by name.</summary>
    public ObservableCollection<LocalModelInfo> Models { get; }

    /// <summary>The folders currently being scanned, in the order they are searched.</summary>
    public IEnumerable<string> SearchFolders
        => new[] { AppPaths.Models }
            .Concat(_config.ExtraModelFolders)
            .Concat(ModelPathsFile.Read())
            .Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Models added one at a time rather than found by a scan.</summary>
    public IEnumerable<string> DirectPaths
        => _config.ExtraModelPaths.Distinct(StringComparer.OrdinalIgnoreCase);

    /// <summary>Rescans every search folder and replaces the contents of <see cref="Models"/>.</summary>
    public void Refresh()
    {
        IsScanning = true;
        try
        {
            var found = new List<LocalModelInfo>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unservable = 0;
            var truncated = false;

            // Named models first, so one added deliberately is never lost to a duplicate found by
            // a scan of the folder it happens to sit in.
            foreach (var path in DirectPaths)
            {
                var descriptor = ModelFormatDetector.Describe(path);

                if (!seen.Add(descriptor.Path))
                {
                    continue;
                }

                if (descriptor.IsServable)
                {
                    found.Add(new LocalModelInfo(descriptor));
                }
                else
                {
                    unservable++;
                }
            }

            foreach (var folder in SearchFolders)
            {
                truncated |= ModelFormatDetector.WouldExceedScanBudget(folder);

                foreach (var descriptor in EnumerateModels(folder))
                {
                    if (!seen.Add(descriptor.Path))
                    {
                        continue;
                    }

                    if (descriptor.IsServable)
                    {
                        found.Add(new LocalModelInfo(descriptor));
                    }
                    else
                    {
                        unservable++;
                    }
                }
            }

            ScanWasTruncated = truncated;

            Models.Clear();
            foreach (var model in found.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
            {
                Models.Add(model);
            }

            UnservableCount = unservable;
        }
        finally
        {
            IsScanning = false;
        }
    }

    /// <summary>Adds a folder to the search set, persists it, and rescans.</summary>
    public CatalogAddition AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return CatalogAddition.Refused("There is no folder at that path.");
        }

        var full = Path.GetFullPath(folder);

        if (SearchFolders.Any(f => string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase)))
        {
            return CatalogAddition.Refused("That folder is already being scanned.");
        }

        _config.ExtraModelFolders.Add(full);
        _config.Save();
        Refresh();

        var added = Models.Count(m => m.Path.StartsWith(full, StringComparison.OrdinalIgnoreCase));

        return CatalogAddition.Success(added switch
        {
            0 => $"Scanning {full}. Nothing in it looks like a model yet.",
            1 => $"Scanning {full}. Found 1 model.",
            _ => $"Scanning {full}. Found {added} models."
        });
    }

    /// <summary>
    /// Adds one model by name: a GGUF file, or a folder holding safetensors weights beside a
    /// config.json. Nothing around it is registered.
    /// </summary>
    /// <remarks>
    /// A refusal here says what is actually wrong with the path, because the alternative is a
    /// file that was picked deliberately and then silently did not appear. The detector already
    /// works out the reason; this hands it on.
    /// </remarks>
    public CatalogAddition AddModel(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return CatalogAddition.Refused("No path was given.");
        }

        var descriptor = ModelFormatDetector.Describe(path);

        if (!descriptor.IsServable)
        {
            return CatalogAddition.Refused(
                descriptor.UnsupportedReason ?? $"{descriptor.DisplayName} is not a model this can serve.");
        }

        var full = Path.GetFullPath(descriptor.Path);

        if (DirectPaths.Any(p => string.Equals(Path.GetFullPath(p), full, StringComparison.OrdinalIgnoreCase)))
        {
            return CatalogAddition.Refused($"{descriptor.DisplayName} is already in the list.");
        }

        _config.ExtraModelPaths.Add(full);
        _config.Save();
        Refresh();

        return CatalogAddition.Success($"Added {descriptor.DisplayName} ({descriptor.FormatLabel}).");
    }

    /// <summary>Stops offering a model that was added by name.</summary>
    public bool RemoveModel(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = Path.GetFullPath(path);

        var match = _config.ExtraModelPaths
            .FirstOrDefault(p => string.Equals(Path.GetFullPath(p), full, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return false;
        }

        _config.ExtraModelPaths.Remove(match);
        _config.Save();
        Refresh();
        return true;
    }

    /// <summary>
    /// Stops scanning a folder that was added here, persists that, and rescans. Returns false for
    /// a folder this does not own.
    /// </summary>
    /// <remarks>
    /// Only folders added through the settings panel can be removed through it. The default models
    /// folder is where a model dropped in with no configuration at all is found, and the ones
    /// listed in model-paths.txt belong to that file: removing either from here would be a change
    /// somewhere the person cannot see it, and would come back the next time the file was read.
    /// </remarks>
    public bool RemoveFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var full = Path.GetFullPath(folder);

        var match = _config.ExtraModelFolders
            .FirstOrDefault(f => string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return false;
        }

        _config.ExtraModelFolders.Remove(match);
        _config.Save();
        Refresh();
        return true;
    }

    /// <summary>True when this folder was added here, and so can be removed here.</summary>
    public bool IsRemovable(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        var full = Path.GetFullPath(folder);

        return _config.ExtraModelFolders
            .Any(f => string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds the catalog entry for a path, or null when it is no longer present.</summary>
    public LocalModelInfo? FindByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Models.FirstOrDefault(m => string.Equals(m.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Everything under a search folder that describes itself as a model.
    /// </summary>
    /// <remarks>
    /// A safetensors model is a folder, so folders are examined as candidates in their own right
    /// and their weight files are not then offered separately. Files are examined by content, so
    /// a GGUF is found whatever it happens to be called.
    /// </remarks>
    private static IEnumerable<ModelDescriptor> EnumerateModels(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var directory in ModelFormatDetector.EnumerateCandidateDirectories(root))
        {
            var folderDescriptor = ModelFormatDetector.Describe(directory);

            if (folderDescriptor.Format is ModelFormat.Safetensors or ModelFormat.SafetensorsComponent)
            {
                yield return folderDescriptor;

                // The weights inside a model folder belong to that folder, so they are not also
                // offered as entries of their own.
                continue;
            }

            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                var descriptor = ModelFormatDetector.Describe(file);

                // Only what detection recognises is reported. Every other file in a models
                // folder, a readme or a tokenizer, is not a failed model and is not mentioned.
                if (descriptor.Format != ModelFormat.Unknown)
                {
                    yield return descriptor;
                }
            }
        }
    }
}
