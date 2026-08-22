using System.IO;
using Microsoft.CodeAnalysis;

namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// Assembles the reference set a Unity script has to be compiled against.
/// </summary>
/// <remarks>
/// Three sources, in this order, first match by file name winning:
/// the project's own <c>Library\ScriptAssemblies</c>, so code can use the types the project
/// already defines; the editor's <c>Managed\UnityEngine</c> modules and <c>UnityEditor.dll</c>,
/// which are the Unity API surface; and the editor's netstandard 2.1 reference assembly, which
/// is what Unity itself compiles game code against.
///
/// The set is cached and rebuilt only when the project's compiled assemblies change, because
/// loading it costs more than the compile and a repair loop compiles several times in a row.
/// Nothing here is written to; the project folder is only read, which is what lets a check run
/// while the Unity editor has the same project open.
///
/// Every way of not finding Unity falls through rather than refusing to compile. A project that
/// is not a Unity project at all goes to <see cref="ProjectReferenceResolver"/>, which reads its
/// own source and its restored packages. A Unity project with no editor installed, or one whose
/// assemblies will not read, falls to <see cref="FrameworkReferenceResolver"/> and still catches
/// every syntax error and every misuse of the standard library. What changes is the reference
/// state, which is what stops a pass being read as more than it is and stops a missing type being
/// read as a mistake.
/// </remarks>
public sealed class UnityReferenceResolver
{
    private readonly object _sync = new();
    private readonly FrameworkReferenceResolver _framework;
    private readonly ProjectReferenceResolver _project = new();

    private CompileReferenceSet? _cached;
    private string? _cachedProject;
    private string? _cachedDataPath;
    private DateTime _cachedStamp;

    public UnityReferenceResolver()
        : this(new FrameworkReferenceResolver())
    {
    }

    public UnityReferenceResolver(FrameworkReferenceResolver framework) => _framework = framework;

    /// <summary>
    /// Returns the reference set for a project, building it if the cached one is stale.
    /// </summary>
    /// <param name="projectPath">The open Unity project, or null when none is open.</param>
    public CompileReferenceSet Resolve(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            // No project is not nothing to check against. The framework alone still catches every
            // syntax error and every misuse of the standard library, which is the difference
            // between this being a Unity tool and being a tool that Unity is one target of.
            return _framework.Resolve();
        }

        // A project that is not a Unity project gets its own source and its own packages, which
        // is the whole of v1.41. Gated on what the project is rather than on whether a Unity
        // install turned up, so a Unity project can never take this path and the Unity numbers
        // cannot move because of it.
        if (Files.ProjectService.Detect(projectPath) != Files.ProjectKind.Unity)
        {
            return _project.Resolve(projectPath, _framework.Resolve(), CancellationToken.None);
        }

        var install = UnityInstallLocator.Resolve(projectPath, out var exactVersion);

        if (install is not { } editor)
        {
            return _framework.Resolve();
        }

        var scriptAssemblies = Path.Combine(projectPath, "Library", "ScriptAssemblies");
        var stamp = NewestWriteTime(scriptAssemblies);

        lock (_sync)
        {
            if (_cached is not null
                && string.Equals(_cachedProject, projectPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(_cachedDataPath, editor.DataPath, StringComparison.OrdinalIgnoreCase)
                && _cachedStamp == stamp)
            {
                return _cached;
            }

            var set = Build(projectPath, editor, exactVersion, scriptAssemblies);

            _cached = set;
            _cachedProject = projectPath;
            _cachedDataPath = editor.DataPath;
            _cachedStamp = stamp;

            return set;
        }
    }

    /// <summary>Drops the cached set, so the next check rebuilds it.</summary>
    public void Invalidate()
    {
        lock (_sync)
        {
            _cached = null;
            _cachedProject = null;
            _cachedDataPath = null;
            _cachedStamp = default;
        }
    }

    private CompileReferenceSet Build(
        string projectPath,
        UnityInstallLocator.UnityInstall editor,
        bool exactVersion,
        string scriptAssemblies)
    {
        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var projectCount = AddFolder(references, seen, scriptAssemblies);

        var unityCount = AddFolder(references, seen, Path.Combine(editor.DataPath, "Managed", "UnityEngine"));
        unityCount += AddFolder(references, seen, Path.Combine(editor.DataPath, "Managed"), "UnityEditor.dll");

        // Unity compiles game code against netstandard 2.1, so that is what the check uses. The
        // application's own framework assemblies are deliberately not referenced: they would let
        // code compile here that Unity would then reject.
        var frameworkCount = AddFolder(references, seen, Path.Combine(editor.DataPath, "NetStandard", "ref", "2.1.0"));

        if (frameworkCount == 0)
        {
            frameworkCount = AddFolder(
                references,
                seen,
                Path.Combine(editor.DataPath, "DotNetSdk", "packs", "NETStandard.Library.Ref", "2.1.0", "ref", "netstandard2.1"));
        }

        if (unityCount == 0 || frameworkCount == 0)
        {
            // Believing there is Unity and then not being able to read it is exactly the state
            // worth surfacing rather than papering over. The check still runs, against the
            // framework, and the summary leads with why it is not running against Unity.
            return _framework.Resolve().WithNote(
                $"The Unity installation at {editor.DataPath} was found but its assemblies could not be read, "
                + "so there is no Unity API to check against.");
        }

        var state = projectCount > 0
            ? CompileReferenceState.Complete
            : CompileReferenceState.ProjectNotCompiled;

        var versionNote = exactVersion
            ? $"Unity {editor.Version}"
            : $"Unity {editor.Version}, which is not the version this project records";

        var summary = state == CompileReferenceState.Complete
            ? $"{versionNote}, plus {projectCount} assembly(ies) this project has already compiled."
            : $"{versionNote}. This project has no compiled assemblies yet, so types it defines itself will not be found. Open it in Unity once to fix that.";

        return new CompileReferenceSet(references, state, summary, editor.Version);
    }

    private static int AddFolder(
        List<MetadataReference> references,
        HashSet<string> seen,
        string folder,
        string pattern = "*.dll")
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(folder, pattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }

        var added = 0;

        foreach (var dll in files)
        {
            if (!seen.Add(Path.GetFileName(dll)))
            {
                continue;
            }

            try
            {
                references.Add(MetadataReference.CreateFromFile(dll));
                added++;
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                // A native library sitting beside the managed ones is not a reference. Unity ships
                // several, and a folder full of them must not stop the check from running.
            }
        }

        return added;
    }

    /// <summary>
    /// When the project's compiled assemblies last changed, which is the only thing that can make
    /// a cached reference set wrong. An absent folder has no time and compares equal to itself.
    /// </summary>
    private static DateTime NewestWriteTime(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return default;
            }

            var newest = default(DateTime);

            foreach (var file in Directory.EnumerateFiles(folder, "*.dll"))
            {
                var written = File.GetLastWriteTimeUtc(file);

                if (written > newest)
                {
                    newest = written;
                }
            }

            return newest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }
}
