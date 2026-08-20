using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>
/// What the opened Unity project already contains, as a symbol index over its C# files.
/// </summary>
/// <remarks>
/// Built by parsing syntax trees in parallel, never through MSBuildWorkspace. That is not a
/// preference: a workspace load runs a design time build per project, it is documented to fail on
/// Unity generated csproj files, and Unity rewrites those files on every recompile. Parsing is
/// lazy, thread safe and cheap enough to repeat.
///
/// The index answers two questions. What already exists, so that a request to build something is
/// not answered by building a second copy of it. And which files are near a request, so that a
/// local model with a small context window is shown the handful that matter rather than the
/// project.
/// </remarks>
public sealed partial class ProjectIndex : ObservableObject
{
    private readonly ProjectIndexCache _cache = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private readonly Dictionary<string, IndexedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<IndexedType>> _typesByName = new(StringComparer.OrdinalIgnoreCase);

    private string? _indexedProject;

    /// <summary>Where the index has got to.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private ProjectIndexState _state = ProjectIndexState.Unknown;

    /// <summary>What it is doing right now, for the panel.</summary>
    [ObservableProperty]
    private string _stage = "Not indexed yet";

    /// <summary>How many files the last index covered.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _fileCount;

    /// <summary>How many types the last index found.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private int _typeCount;

    /// <summary>How many files the last index had to reparse rather than take from the cache.</summary>
    [ObservableProperty]
    private int _reparsedCount;

    /// <summary>How long the last index took.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private TimeSpan _lastDuration;

    /// <summary>True once there is an index to ask questions of.</summary>
    public bool IsReady => State is ProjectIndexState.Ready or ProjectIndexState.Empty;

    /// <summary>One line for the panel and the feed.</summary>
    public string StatusText => State switch
    {
        ProjectIndexState.Indexing => "Reading the project.",
        ProjectIndexState.Ready => $"{TypeCount} type(s) across {FileCount} file(s), read in {LastDuration.TotalSeconds:0.0} s.",
        ProjectIndexState.Empty => "The project has no C# files yet.",
        ProjectIndexState.Unavailable => "No project is open, so nothing is known about what it contains.",
        _ => "Not indexed yet."
    };

    /// <summary>Every file the last index covered.</summary>
    public IReadOnlyCollection<IndexedFile> Files => _files.Values;

    /// <summary>The project the current index belongs to, or null.</summary>
    public string? IndexedProject => _indexedProject;

    /// <summary>
    /// Brings the index up to date for a project. Cheap when nothing has changed, because every
    /// file whose write time and length still match is taken from the cache.
    /// </summary>
    public async Task EnsureAsync(string? projectPath, IProgress<string>? status, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            Set(ProjectIndexState.Unavailable, "No project open");
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var assets = Path.Combine(projectPath, "Assets");

            if (!Directory.Exists(assets))
            {
                Set(ProjectIndexState.Unavailable, "No Assets folder");
                return;
            }

            State = ProjectIndexState.Indexing;
            Stage = "Listing scripts";
            status?.Report("Reading the project");

            var stopwatch = Stopwatch.StartNew();
            var sources = EnumerateScripts(assets, projectPath);

            var cached = string.Equals(_indexedProject, projectPath, StringComparison.OrdinalIgnoreCase) && _files.Count > 0
                ? new Dictionary<string, IndexedFile>(_files, StringComparer.OrdinalIgnoreCase)
                : _cache.Read(projectPath) as IDictionary<string, IndexedFile> ?? new Dictionary<string, IndexedFile>(StringComparer.OrdinalIgnoreCase);

            Stage = $"Reading {sources.Count} script(s)";

            var parsed = new ConcurrentBag<IndexedFile>();
            var reparsed = 0;

            await Task.Run(
                () => Parallel.ForEach(
                    sources,
                    new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                    source =>
                    {
                        if (cached.TryGetValue(source.Relative, out var existing)
                            && existing.LastWriteUtc == source.LastWriteUtc
                            && existing.Length == source.Length)
                        {
                            parsed.Add(existing);
                            return;
                        }

                        var file = SourceFileParser.Parse(source.Absolute, source.Relative, ct);

                        if (file is not null)
                        {
                            parsed.Add(file);
                            Interlocked.Increment(ref reparsed);
                        }
                    }),
                ct).ConfigureAwait(false);

            stopwatch.Stop();

            Replace(parsed);
            _indexedProject = projectPath;

            FileCount = _files.Count;
            TypeCount = _files.Values.Sum(f => f.Types.Count);
            ReparsedCount = reparsed;
            LastDuration = stopwatch.Elapsed;

            _cache.Write(projectPath, _files.Values);

            State = _files.Count == 0 ? ProjectIndexState.Empty : ProjectIndexState.Ready;
            Stage = StatusText;
            status?.Report($"{StatusText} {reparsed} of them had to be read again.");
        }
        catch (OperationCanceledException)
        {
            Set(ProjectIndexState.Unknown, "Cancelled");
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Set(ProjectIndexState.Unavailable, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Every declaration of a type with this name, across the project.</summary>
    public IReadOnlyList<IndexedType> FindType(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Array.Empty<IndexedType>();
        }

        var bare = name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : name;

        return _typesByName.TryGetValue(bare, out var types)
            ? types
            : Array.Empty<IndexedType>();
    }

    /// <summary>The file a type is declared in, or null when the project does not declare it.</summary>
    public IndexedFile? FileOf(IndexedType type)
        => _files.Values.FirstOrDefault(f => f.Types.Contains(type));

    /// <summary>Looks a file up by its project relative path.</summary>
    public IndexedFile? FindFile(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        return _files.TryGetValue(Normalise(relativePath), out var file) ? file : null;
    }

    /// <summary>Drops what is held in memory, so the next call reads from disk again.</summary>
    public void Forget()
    {
        _files.Clear();
        _typesByName.Clear();
        _indexedProject = null;

        FileCount = 0;
        TypeCount = 0;
        LastDuration = TimeSpan.Zero;
        Set(ProjectIndexState.Unknown, "Not indexed yet");
    }

    /// <summary>Normalises a project relative path to the form the index stores.</summary>
    public static string Normalise(string relativePath)
        => relativePath.Replace('\\', '/').TrimStart('/');

    private void Replace(IEnumerable<IndexedFile> files)
    {
        _files.Clear();
        _typesByName.Clear();

        foreach (var file in files)
        {
            _files[file.RelativePath] = file;

            foreach (var type in file.Types)
            {
                if (!_typesByName.TryGetValue(type.Name, out var list))
                {
                    list = new List<IndexedType>();
                    _typesByName[type.Name] = list;
                }

                list.Add(type);
            }
        }
    }

    private void Set(ProjectIndexState state, string stage)
    {
        State = state;
        Stage = stage;
    }

    /// <summary>
    /// The scripts worth indexing. Anything Unity treats as a package cache or a build artefact
    /// is skipped, because none of it is code the user is asking about.
    /// </summary>
    private static List<SourceRef> EnumerateScripts(string assets, string projectPath)
    {
        var found = new List<SourceRef>();

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
        };

        foreach (var path in Directory.EnumerateFiles(assets, "*.cs", options))
        {
            var relative = Normalise(Path.GetRelativePath(projectPath, path));

            if (IsIgnored(relative))
            {
                continue;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            found.Add(new SourceRef(path, relative, info.LastWriteTimeUtc, info.Length));
        }

        return found;
    }

    /// <summary>
    /// Folders Unity itself ignores. A folder whose name ends in a tilde or starts with a dot is
    /// not compiled into the project, so indexing it would offer the user code that cannot run.
    /// </summary>
    private static bool IsIgnored(string relativePath)
        => relativePath.Split('/').Any(segment => segment.EndsWith('~') || segment.StartsWith('.'));

    private readonly record struct SourceRef(string Absolute, string Relative, DateTime LastWriteUtc, long Length);
}
