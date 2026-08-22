using System.IO;
using Microsoft.CodeAnalysis;

namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// Every framework assembly this application is running on, loaded once.
/// </summary>
/// <remarks>
/// The whole platform rather than the short list <see cref="FrameworkReferenceResolver"/> uses,
/// and the difference is deliberate. That list is a floor: it exists so that a check can still
/// catch a syntax error when there is nothing else at all to compile against, and seventeen
/// assemblies load fast. This is for the case where the answer is supposed to be trusted, and a
/// project that happens to use an assembly outside that seventeen would otherwise be told a type is
/// missing when it is not. A phantom missing type is exactly what v1.41 exists to stop producing,
/// so it would be a poor thing to introduce a new source of one.
///
/// Read through the trusted platform assemblies list rather than through <c>Assembly.Location</c>,
/// which is the same trap the floor documents: a single file executable keeps its assemblies inside
/// itself and one loaded from there reports no location at all.
///
/// Loaded once for the life of the process, because the framework does not change underneath a
/// running application and metadata is expensive enough that a repair loop would notice.
/// </remarks>
public static class PlatformReferences
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> Loaded = new(Load, isThreadSafe: true);

    /// <summary>Every platform assembly that exists as a file, as references.</summary>
    public static IReadOnlyList<MetadataReference> All => Loaded.Value;

    private static IReadOnlyList<MetadataReference> Load()
    {
        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is not string listed || listed.Length == 0)
        {
            return Array.Empty<MetadataReference>();
        }

        var references = new List<MetadataReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in listed.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Path.GetFileName(path);

            // The list can name the same assembly twice, and Roslyn refuses a compilation holding
            // two references to one assembly identity.
            if (!seen.Add(name) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or NotSupportedException)
            {
                // One assembly that will not read is one reference short, not a failure of the set.
            }
        }

        return references;
    }
}
