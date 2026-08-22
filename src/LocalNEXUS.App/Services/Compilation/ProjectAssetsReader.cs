using System.IO;
using System.Text.Json;

namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// What a restore left behind: every package assembly the project compiles against.
/// </summary>
/// <remarks>
/// Read from <c>obj/project.assets.json</c> rather than from the project file, and the choice is
/// worth writing down because both were candidates.
///
/// A csproj is always there and names its packages, but it names them as an identifier and a
/// version range. Turning that into a path on this disk means resolving the version, walking
/// transitive dependencies, knowing the layout of the package cache, and picking the right lib
/// folder for the target framework out of the several a package ships. That is NuGet's job, it is
/// a great deal of behaviour to reimplement, and every part of it can be subtly wrong in a way that
/// produces a phantom missing type.
///
/// The assets file is the answer to all of that, already computed. Restore writes it, it names the
/// package cache roots it used, and for each library it lists exactly which files are the compile
/// time ones for the framework being built. Reading it is a dictionary lookup rather than a
/// resolution algorithm.
///
/// What it costs is that it only exists after a restore. A project nobody has built has no assets
/// file, and that is an ordinary state rather than a failure: the caller falls back to the
/// project's own source with no packages, says so, and goes on refusing to trust a missing type.
///
/// No MSBuild. v1.11 settled that for the index and the reasoning is the same here: a design time
/// build costs minutes on a real solution and is documented to fail on generated project files.
/// This reads a file that a build already wrote.
/// </remarks>
public static class ProjectAssetsReader
{
    /// <summary>Where restore puts it, relative to the project folder.</summary>
    private const string AssetsPath = "obj/project.assets.json";

    /// <summary>What was found, or why nothing was.</summary>
    /// <param name="Assemblies">Absolute paths to the package assemblies, deduplicated.</param>
    /// <param name="Found">True when an assets file was read.</param>
    /// <param name="Note">One sentence for the feed, whether or not anything was found.</param>
    /// <param name="ProjectReferences">
    /// How many entries named another project rather than a package. Reported rather than
    /// resolved: a sibling project's output is a build artefact that may not exist, and following
    /// one properly means resolving its assets file too. Counted so the summary can say what was
    /// left out instead of quietly leaving it out.
    /// </param>
    public readonly record struct Assets(
        IReadOnlyList<string> Assemblies,
        bool Found,
        string Note,
        int ProjectReferences);

    /// <summary>Finds the assets file for a project folder and reads what it names.</summary>
    public static Assets Read(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return new Assets(Array.Empty<string>(), false, "No project is open.", 0);
        }

        var path = Path.Combine(projectPath, AssetsPath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            return new Assets(
                Array.Empty<string>(),
                false,
                "This project has not been restored, so nothing names its packages. Run a build or "
                + "a restore and the check will pick them up.",
                0);
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);

            return ReadDocument(document.RootElement);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new Assets(
                Array.Empty<string>(),
                false,
                $"The restore record could not be read, so no packages are available: {ex.Message}",
                0);
        }
    }

    private static Assets ReadDocument(JsonElement root)
    {
        var folders = PackageFolders(root);

        if (folders.Count == 0)
        {
            return new Assets(
                Array.Empty<string>(),
                true,
                "The restore record names no package folders, so no package could be located.",
                0);
        }

        if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
        {
            return new Assets(Array.Empty<string>(), true, "The restore record names no targets.", 0);
        }

        var libraries = Libraries(root);

        var assemblies = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var projectReferences = 0;
        var missing = 0;

        // The first target, which is the framework the project was restored for. A project
        // restored for several is rare outside library authoring, and taking the first is what a
        // build does when nothing says otherwise.
        foreach (var target in targets.EnumerateObject().Take(1))
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (library.Value.TryGetProperty("type", out var type)
                    && string.Equals(type.GetString(), "project", StringComparison.OrdinalIgnoreCase))
                {
                    projectReferences++;
                    continue;
                }

                if (!library.Value.TryGetProperty("compile", out var compile)
                    || compile.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!libraries.TryGetValue(library.Name, out var relativeFolder))
                {
                    continue;
                }

                foreach (var item in compile.EnumerateObject())
                {
                    // A package that deliberately contributes nothing for this framework marks it
                    // with an underscore placeholder rather than omitting the entry.
                    if (Path.GetFileName(item.Name) is "_._")
                    {
                        continue;
                    }

                    var resolved = Locate(folders, relativeFolder, item.Name);

                    if (resolved is null)
                    {
                        missing++;
                        continue;
                    }

                    if (seen.Add(resolved))
                    {
                        assemblies.Add(resolved);
                    }
                }
            }
        }

        var note = missing == 0
            ? $"{assemblies.Count} package assembly(ies) from the restore record."
            : $"{assemblies.Count} package assembly(ies) from the restore record, and {missing} the "
              + "record named that are not on this disk.";

        if (projectReferences > 0)
        {
            note += $" {projectReferences} reference(s) to other projects were left out, because a "
                    + "sibling project's output is a build artefact rather than something to read.";
        }

        return new Assets(assemblies, true, note, projectReferences);
    }

    /// <summary>The cache roots the restore used, in the order it listed them.</summary>
    private static IReadOnlyList<string> PackageFolders(JsonElement root)
    {
        if (!root.TryGetProperty("packageFolders", out var folders) || folders.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<string>();
        }

        return folders.EnumerateObject().Select(f => f.Name).ToList();
    }

    /// <summary>Each library, mapped to the folder it sits in inside a cache root.</summary>
    private static IReadOnlyDictionary<string, string> Libraries(JsonElement root)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Object)
        {
            return map;
        }

        foreach (var library in libraries.EnumerateObject())
        {
            if (library.Value.TryGetProperty("path", out var path) && path.GetString() is { Length: > 0 } value)
            {
                map[library.Name] = value;
            }
        }

        return map;
    }

    /// <summary>The first cache root that actually holds this file.</summary>
    private static string? Locate(IReadOnlyList<string> folders, string packageFolder, string relativeFile)
    {
        foreach (var root in folders)
        {
            var candidate = Path.GetFullPath(Path.Combine(
                root,
                packageFolder.Replace('/', Path.DirectorySeparatorChar),
                relativeFile.Replace('/', Path.DirectorySeparatorChar)));

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
