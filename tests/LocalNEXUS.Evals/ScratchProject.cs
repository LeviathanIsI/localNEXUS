using System.IO;

namespace LocalNEXUS.Evals;

/// <summary>
/// A throwaway project, built for one task and deleted after it.
/// </summary>
/// <remarks>
/// Generated rather than pointed at anything real, and generated fresh for every task and every
/// repeat, because a run that leaves the project changed makes the next one measure something
/// else. This is the harness's own copy of the rule the test suite follows: under the system temp
/// folder, never in the repository, never a real project, never anywhere the application keeps its
/// own data.
///
/// Either shape. A Unity one carries ProjectSettings and a .cs.meta sibling beside every script,
/// because the binding rules read the meta and a project without one would not exercise them. A
/// plain one carries a project file and, more to the point, nothing that would make detection call
/// it Unity: no Assets folder, no ProjectSettings, no package manifest.
/// </remarks>
public sealed class ScratchProject : IDisposable
{
    /// <summary>Folder names that hold something other than the project's own source.</summary>
    private static readonly string[] NotSource = { "bin", "obj", "node_modules", ".git" };

    private readonly Dictionary<string, string> _before = new(StringComparer.OrdinalIgnoreCase);

    private ScratchProject(string root, ProjectShape shape)
    {
        Root = root;
        Shape = shape;
    }

    /// <summary>The project folder.</summary>
    public string Root { get; }

    /// <summary>What sort of project this is.</summary>
    public ProjectShape Shape { get; }

    /// <summary>Builds one from a task's seed.</summary>
    public static ScratchProject Create(EvalTask task)
    {
        var root = Path.Combine(Path.GetTempPath(), "localnexus-evals", Guid.NewGuid().ToString("N"));
        var project = new ScratchProject(root, task.Project);

        Directory.CreateDirectory(root);

        if (task.Project == ProjectShape.Unity)
        {
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));

            // Enough for the locator to believe this is a Unity project without one being
            // installed.
            File.WriteAllText(
                Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"),
                "m_EditorVersion: 2022.3.20f1" + Environment.NewLine);
        }
        else
        {
            // A project file, because that is what makes a folder of C# a project to a person.
            // Nothing in the application reads it: the compile check has no way to resolve a plain
            // project's references and falls back to the framework, which is the limitation this
            // set exists to measure rather than to hide.
            File.WriteAllText(
                Path.Combine(root, "Shop.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Nullable>enable</Nullable>
                  </PropertyGroup>
                </Project>
                """.ReplaceLineEndings());
        }

        foreach (var seed in task.Seed)
        {
            var absolute = project.Absolute(seed.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, seed.Content.ReplaceLineEndings());

            if (task.Project == ProjectShape.Unity)
            {
                // Every script Unity has imported has one of these, and the GUID in it is what
                // scenes reference. A rename rule that never saw one would never fire.
                File.WriteAllText(
                    absolute + ".meta",
                    "fileFormatVersion: 2" + Environment.NewLine
                    + $"guid: {Guid.NewGuid():N}" + Environment.NewLine);
            }

            project._before[Normalise(seed.RelativePath)] = seed.Content.ReplaceLineEndings();
        }

        return project;
    }

    /// <summary>
    /// Every C# file in the project now, by path relative to the root.
    /// </summary>
    /// <remarks>
    /// Scanned from where the index scans from, which is Assets for a Unity project and the root
    /// for anything else. Reading from a different root than the application uses would mean
    /// measuring files it never saw, or missing files it wrote.
    /// </remarks>
    public IReadOnlyDictionary<string, string> ReadAllScripts()
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var scanRoot = Shape == ProjectShape.Unity ? Path.Combine(Root, "Assets") : Root;

        if (!Directory.Exists(scanRoot))
        {
            return found;
        }

        foreach (var file in Directory.EnumerateFiles(scanRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Normalise(Path.GetRelativePath(Root, file));

            if (relative.Split('/').Any(s => NotSource.Contains(s, StringComparer.OrdinalIgnoreCase)))
            {
                continue;
            }

            found[relative] = File.ReadAllText(file);
        }

        return found;
    }

    /// <summary>Files that were not there when the task started.</summary>
    public IReadOnlyList<string> NewFiles()
        => ReadAllScripts().Keys.Where(p => !_before.ContainsKey(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Files that were there and whose contents are not what they were.
    /// </summary>
    /// <remarks>
    /// Line endings are normalised before comparing, and that is not a detail. A model that
    /// returns a file unchanged apart from writing it with different newlines had produced no edit
    /// at all, and counting it as one made a task that the model silently declined to do look like
    /// one it had completed. That was measured rather than reasoned about: the refusal task
    /// reported an edit landing while the file came back byte for byte the same code.
    /// </remarks>
    public IReadOnlyList<string> ChangedFiles()
    {
        var now = ReadAllScripts();

        return _before
            .Where(pair => now.TryGetValue(pair.Key, out var content)
                           && !string.Equals(Canonical(content), Canonical(pair.Value), StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The text with its newlines made uniform, so only real changes count.</summary>
    private static string Canonical(string content) => content.ReplaceLineEndings("\n").TrimEnd();

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
        => Shape != ProjectShape.Unity
            ? Array.Empty<string>()
            : _before.Keys
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
