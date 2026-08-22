using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// Assembles what a check should compile against for a project that is not a Unity project.
/// </summary>
/// <remarks>
/// Three parts, and only the first was there before v1.41.
///
/// The framework, which is the assemblies this application is itself running on. In this mode that
/// is every one of them rather than the short list the framework only floor uses, because a floor
/// wants to be cheap and this wants to be right: a project using an assembly outside a curated
/// seventeen would otherwise get a phantom missing type, and a phantom is exactly what this whole
/// change exists to stop producing.
///
/// The project's packages, read out of the record a restore leaves rather than resolved from the
/// project file. <see cref="ProjectAssetsReader"/> says why at length.
///
/// The project's own source, parsed and handed over as a reference rather than as more files in the
/// compilation. <see cref="ProjectSourceSet"/> says why.
///
/// All of it is cached and rebuilt only when the project's own files change, because a repair loop
/// compiles several times in a row and parsing a real project on every attempt would be the slowest
/// thing in the run. The stamp is the newest write time and the file count, which is the same cheap
/// test the index cache uses.
/// </remarks>
public sealed class ProjectReferenceResolver
{
    /// <summary>
    /// The language version a project outside Unity is read at.
    /// </summary>
    /// <remarks>
    /// The latest the bundled Roslyn understands. Holding an ordinary project to the version Unity
    /// accepts would report file scoped namespaces and records as syntax errors, which its own
    /// build compiles without comment.
    /// </remarks>
    public const LanguageVersion Language = LanguageVersion.Latest;

    /// <summary>Folders that hold something other than the project's own source.</summary>
    private static readonly string[] NotSource = { "bin", "obj", "node_modules", "packages", "dist", "out", "target" };

    private readonly object _sync = new();

    private CompileReferenceSet? _cached;
    private string? _cachedProject;
    private Stamp _cachedStamp;

    /// <summary>
    /// The reference set for a project, built if the cached one is stale.
    /// </summary>
    /// <remarks>
    /// Never throws. Every way of finding less than hoped for produces a set that says what it has,
    /// because a project nobody has restored and a project whose source will not read are both
    /// ordinary and neither is a reason to refuse to check anything.
    /// </remarks>
    public CompileReferenceSet Resolve(string projectPath, CompileReferenceSet framework, CancellationToken ct)
    {
        var stamp = StampOf(projectPath);

        lock (_sync)
        {
            if (_cached is not null
                && string.Equals(_cachedProject, projectPath, StringComparison.OrdinalIgnoreCase)
                && _cachedStamp == stamp)
            {
                return _cached;
            }

            var built = Build(projectPath, framework, ct);

            _cached = built;
            _cachedProject = projectPath;
            _cachedStamp = stamp;

            return built;
        }
    }

    private static CompileReferenceSet Build(string projectPath, CompileReferenceSet framework, CancellationToken ct)
    {
        var references = new List<MetadataReference>(PlatformReferences.All);

        if (references.Count == 0)
        {
            // Nothing at all to compile against, which a single file build can legitimately be.
            // The floor's own answer is the honest one and there is nothing to add to it.
            return framework;
        }

        var assets = ProjectAssetsReader.Read(projectPath);

        foreach (var assembly in assets.Assemblies)
        {
            try
            {
                references.Add(MetadataReference.CreateFromFile(assembly));
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                // A package assembly that will not read is one missing reference, not a reason to
                // abandon the set.
            }
        }

        var sources = ReadSource(projectPath, ct);

        var state = assets.Found
            ? CompileReferenceState.ProjectResolved
            : CompileReferenceState.ProjectNotRestored;

        var summary = state == CompileReferenceState.ProjectResolved
            ? $"The project's own source and its restored packages: {sources.Summary}, {assets.Note} "
              + "A type that cannot be found here is a type that is not there."
            : $"The project's own source, and no packages. {assets.Note} {sources.Summary}. "
              + "A missing type may still be a package this check could not see, so an error "
              + "blaming one is not trusted.";

        return new CompileReferenceSet(references, state, summary, null, sources, Language);
    }

    /// <summary>Every C# file the project owns, read from disk.</summary>
    private static ProjectSourceSet ReadSource(string projectPath, CancellationToken ct)
    {
        var files = new List<(string Path, string Text)>();

        foreach (var path in EnumerateSource(projectPath))
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                files.Add((path, File.ReadAllText(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // One unreadable file is one type the check will not know about, which is the
                // state this was in for every file before.
            }
        }

        return files.Count == 0 ? ProjectSourceSet.Empty : ProjectSourceSet.Parse(files, Language, ct);
    }

    private static IEnumerable<string> EnumerateSource(string projectPath)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        IEnumerable<string> found;

        try
        {
            found = Directory.EnumerateFiles(projectPath, "*.cs", options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        return found.Where(path =>
        {
            var relative = Path.GetRelativePath(projectPath, path).Replace('\\', '/');

            return !relative.Split('/').Any(segment =>
                segment.StartsWith('.')
                || NotSource.Contains(segment, StringComparer.OrdinalIgnoreCase));
        });
    }

    /// <summary>
    /// Enough about the project's source to tell whether it has changed.
    /// </summary>
    /// <remarks>
    /// Counted and timed rather than hashed. Reading every file to decide whether to read every
    /// file is the thing this cache exists to avoid, and a write time and a count together move
    /// for every edit that matters.
    /// </remarks>
    private static Stamp StampOf(string projectPath)
    {
        var count = 0;
        var newest = DateTime.MinValue;

        foreach (var path in EnumerateSource(projectPath))
        {
            count++;

            try
            {
                var written = File.GetLastWriteTimeUtc(path);

                if (written > newest)
                {
                    newest = written;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Counted but not timed, which is enough to notice it appearing or going away.
            }
        }

        return new Stamp(count, newest);
    }

    private readonly record struct Stamp(int Count, DateTime Newest);
}
