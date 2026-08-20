using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>
/// Keeps a parsed index on disk so a second session does not reparse a project that has not
/// changed.
/// </summary>
/// <remarks>
/// Invalidation is per file, not wholesale: an entry survives while its write time and length
/// both match what is on disk, so editing one script reparses one script. The same rule the
/// compile checker's reference cache uses, for the same reason.
///
/// A cache that cannot be read is not an error. It is a cold start, which is slower and correct.
/// </remarks>
public sealed class ProjectIndexCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Bumped when the shape of what is parsed changes, which retires every old cache.</summary>
    private const int FormatVersion = 1;

    /// <summary>Where the caches live, one file per project.</summary>
    private static string CacheFolder => Path.Combine(AppPaths.Runtime, "index");

    /// <summary>Reads the cached index for a project, or null when there is not a usable one.</summary>
    public IReadOnlyDictionary<string, IndexedFile>? Read(string projectPath)
    {
        try
        {
            var file = PathFor(projectPath);

            if (!File.Exists(file))
            {
                return null;
            }

            var document = JsonSerializer.Deserialize<CacheDocument>(File.ReadAllText(file), SerializerOptions);

            if (document is null
                || document.FormatVersion != FormatVersion
                || !string.Equals(document.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return document.Files.ToDictionary(f => f.RelativePath, Hydrate, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes the index for a project. Failing to write costs a slower next start.</summary>
    public void Write(string projectPath, IEnumerable<IndexedFile> files)
    {
        try
        {
            Directory.CreateDirectory(CacheFolder);

            var document = new CacheDocument
            {
                FormatVersion = FormatVersion,
                ProjectPath = projectPath,
                Files = files.Select(Dehydrate).ToList()
            };

            File.WriteAllText(PathFor(projectPath), JsonSerializer.Serialize(document, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The index is a cache. Losing it is a slower start and nothing else.
        }
    }

    /// <summary>Removes the cached index for a project.</summary>
    public void Clear(string projectPath)
    {
        try
        {
            var file = PathFor(projectPath);

            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing here is worth failing over.
        }
    }

    /// <summary>
    /// One file per project, named by a hash of its path so that two projects with the same
    /// folder name do not share a cache and no path ends up in a file name.
    /// </summary>
    private static string PathFor(string projectPath)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(projectPath.ToLowerInvariant()));
        return Path.Combine(CacheFolder, Convert.ToHexString(bytes)[..16] + ".json");
    }

    private static IndexedFile Hydrate(CachedFile file)
        => new(
            file.RelativePath,
            file.LastWriteUtc,
            file.Length,
            file.Namespace ?? string.Empty,
            file.Types.Select(Hydrate).ToList(),
            file.References ?? new List<string>());

    private static IndexedType Hydrate(CachedType type)
        => new(
            type.Name,
            type.Namespace ?? string.Empty,
            type.Kind,
            type.BaseTypes ?? new List<string>(),
            (type.Members ?? new List<CachedMember>())
                .Select(m => new IndexedMember(m.Kind, m.Name, m.Signature, m.IsSerialized))
                .ToList(),
            type.IsPartial,
            type.Line);

    private static CachedFile Dehydrate(IndexedFile file)
        => new()
        {
            RelativePath = file.RelativePath,
            LastWriteUtc = file.LastWriteUtc,
            Length = file.Length,
            Namespace = file.Namespace,
            Types = file.Types.Select(Dehydrate).ToList(),
            References = file.ReferencedTypeNames.ToList()
        };

    private static CachedType Dehydrate(IndexedType type)
        => new()
        {
            Name = type.Name,
            Namespace = type.Namespace,
            Kind = type.Kind,
            BaseTypes = type.BaseTypes.ToList(),
            Members = type.Members
                .Select(m => new CachedMember { Kind = m.Kind, Name = m.Name, Signature = m.Signature, IsSerialized = m.IsSerialized })
                .ToList(),
            IsPartial = type.IsPartial,
            Line = type.Line
        };

    private sealed class CacheDocument
    {
        public int FormatVersion { get; set; }

        public string ProjectPath { get; set; } = string.Empty;

        public List<CachedFile> Files { get; set; } = new();
    }

    private sealed class CachedFile
    {
        public string RelativePath { get; set; } = string.Empty;

        public DateTime LastWriteUtc { get; set; }

        public long Length { get; set; }

        public string? Namespace { get; set; }

        public List<CachedType> Types { get; set; } = new();

        public List<string>? References { get; set; }
    }

    private sealed class CachedType
    {
        public string Name { get; set; } = string.Empty;

        public string? Namespace { get; set; }

        public IndexedTypeKind Kind { get; set; }

        public List<string>? BaseTypes { get; set; }

        public List<CachedMember>? Members { get; set; }

        public bool IsPartial { get; set; }

        public int Line { get; set; }
    }

    private sealed class CachedMember
    {
        public IndexedMemberKind Kind { get; set; }

        public string Name { get; set; } = string.Empty;

        public string Signature { get; set; } = string.Empty;

        public bool IsSerialized { get; set; }
    }
}
