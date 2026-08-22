using System.IO;

namespace LocalNEXUS.Evals;

/// <summary>
/// A throwaway Unity shaped project, built for one task and deleted after it.
/// </summary>
/// <remarks>
/// Generated rather than pointed at anything real, and generated fresh for every task and every
/// repeat, because a run that leaves the project changed makes the next one measure something
/// else. This is the harness's own copy of the rule the test suite follows: under the system temp
/// folder, never in the repository, never a real project, never anywhere the application keeps its
/// own data.
///
/// It carries the .cs.meta sibling for the component, because the Unity binding rules read it and
/// a project without one would not exercise them.
/// </remarks>
public sealed class ScratchProject : IDisposable
{
    private readonly Dictionary<string, string> _before = new(StringComparer.OrdinalIgnoreCase);

    private ScratchProject(string root) => Root = root;

    /// <summary>The project folder, which stands in for a Unity project root.</summary>
    public string Root { get; }

    /// <summary>Builds one from a task's seed.</summary>
    public static ScratchProject Create(EvalTask task)
    {
        var root = Path.Combine(Path.GetTempPath(), "localnexus-evals", Guid.NewGuid().ToString("N"));
        var project = new ScratchProject(root);

        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));

        // Enough for the locator to believe this is a Unity project without one being installed.
        File.WriteAllText(
            Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"),
            "m_EditorVersion: 2022.3.20f1" + Environment.NewLine);

        foreach (var seed in task.Seed)
        {
            var absolute = project.Absolute(seed.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, seed.Content.ReplaceLineEndings());

            // Every script Unity has imported has one of these, and the GUID in it is what scenes
            // reference. A rename rule that never saw one would never fire.
            File.WriteAllText(
                absolute + ".meta",
                "fileFormatVersion: 2" + Environment.NewLine
                + $"guid: {Guid.NewGuid():N}" + Environment.NewLine);

            project._before[Normalise(seed.RelativePath)] = seed.Content.ReplaceLineEndings();
        }

        return project;
    }

    /// <summary>Every C# file in the project now, by path relative to the root.</summary>
    public IReadOnlyDictionary<string, string> ReadAllScripts()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assets = Path.Combine(Root, "Assets");

        if (!Directory.Exists(assets))
        {
            return found;
        }

        foreach (var file in Directory.EnumerateFiles(assets, "*.cs", SearchOption.AllDirectories))
        {
            found[Normalise(Path.GetRelativePath(Root, file))] = File.ReadAllText(file);
        }

        return found;
    }

    /// <summary>Files that were not there when the task started.</summary>
    public IReadOnlyList<string> NewFiles()
        => ReadAllScripts().Keys.Where(p => !_before.ContainsKey(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();

    /// <summary>Files that were there and whose contents are not what they were.</summary>
    public IReadOnlyList<string> ChangedFiles()
    {
        var now = ReadAllScripts();

        return _before
            .Where(pair => now.TryGetValue(pair.Key, out var content) && !string.Equals(content, pair.Value, StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Files that were there when the task started and are not now.</summary>
    public IReadOnlyList<string> DeletedFiles()
    {
        var now = ReadAllScripts();

        return _before.Keys.Where(p => !now.ContainsKey(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
    }

    /// <summary>Whether a meta file still sits beside every script that had one.</summary>
    /// <remarks>
    /// A write that deleted and recreated a script would issue a new GUID and quietly detach it
    /// from every scene. Cheap to check and catastrophic to miss, so it is checked.
    /// </remarks>
    public IReadOnlyList<string> ScriptsMissingTheirMeta()
        => _before.Keys
            .Where(p => File.Exists(Absolute(p)) && !File.Exists(Absolute(p) + ".meta"))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

    private string Absolute(string relativePath)
        => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string Normalise(string path) => path.Replace('\\', '/');

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that will not delete is not a result. It is under the system temp
            // folder and goes with everything else there.
        }
    }
}
