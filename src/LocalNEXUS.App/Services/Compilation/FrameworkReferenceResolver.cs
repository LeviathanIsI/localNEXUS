using System.IO;
using Microsoft.CodeAnalysis;

namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// Assembles a reference set from the framework this application is running on, for checking code
/// when there is no ecosystem to check it against.
/// </summary>
/// <remarks>
/// This is the honest floor of what a compile can prove. With no Unity and no project assemblies,
/// a check still catches every syntax error and every misuse of the standard library, which is a
/// large share of what a model gets wrong and is a great deal better than writing the file and
/// finding out later. What it cannot see is any type the surrounding project defines, and the
/// reference state says so rather than letting a pass read as more than it is.
///
/// It is also what makes this application more than a Unity tool. A run against a plain C# project
/// gets a real compile check rather than being told there is no Unity installation.
///
/// The assemblies are found through the trusted platform assemblies list rather than through
/// <c>Assembly.Location</c>, and that is the whole difficulty. A single file executable keeps its
/// assemblies inside itself, and one loaded from there reports no location at all, so asking for
/// it hands back an empty string and Roslyn throws. The same trap took the published build down
/// once already, in the script transform. The list is read, every entry is checked for a file that
/// actually exists, and a build where none of them do reports that plainly instead of pretending
/// it can compile.
/// </remarks>
public sealed class FrameworkReferenceResolver
{
    /// <summary>
    /// The assemblies a piece of ordinary C# needs before it can reference anything else. An entry
    /// missing from the platform list is skipped rather than failing the set, because the check is
    /// worth running on whatever is there.
    /// </summary>
    private static readonly string[] Wanted =
    {
        "System.Private.CoreLib.dll",
        "System.Runtime.dll",
        "System.Console.dll",
        "System.Collections.dll",
        "System.Linq.dll",
        "System.Linq.Expressions.dll",
        "System.Text.RegularExpressions.dll",
        "System.Runtime.Extensions.dll",
        "System.Threading.dll",
        "System.Threading.Tasks.dll",
        "System.Memory.dll",
        "netstandard.dll",
        "System.Collections.Concurrent.dll",
        "System.ObjectModel.dll",
        "System.ComponentModel.dll",
        "System.ComponentModel.Primitives.dll",
        "System.Runtime.InteropServices.dll"
    };

    private readonly object _sync = new();

    private CompileReferenceSet? _cached;

    /// <summary>
    /// The framework only reference set, built once and reused.
    /// </summary>
    public CompileReferenceSet Resolve()
    {
        lock (_sync)
        {
            return _cached ??= Build();
        }
    }

    private static CompileReferenceSet Build()
    {
        var listed = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        if (string.IsNullOrWhiteSpace(listed))
        {
            return CompileReferenceSet.Unavailable(
                CompileReferenceState.NoFrameworkReferences,
                "This build cannot reach its own framework assemblies, so there is nothing at all to compile against.");
        }

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in listed.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = Path.GetFileName(path);

            if (!byName.ContainsKey(name))
            {
                byName[name] = path;
            }
        }

        var references = new List<MetadataReference>();
        var missing = 0;

        foreach (var name in Wanted)
        {
            if (!byName.TryGetValue(name, out var path) || !File.Exists(path))
            {
                missing++;
                continue;
            }

            try
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or NotSupportedException)
            {
                missing++;
            }
        }

        if (references.Count == 0)
        {
            return CompileReferenceSet.Unavailable(
                CompileReferenceState.NoFrameworkReferences,
                "None of this build's framework assemblies exist as files on disk, so there is nothing to compile against. "
                + "A single file build keeps them inside the executable unless it extracts them first.");
        }

        var note = missing == 0
            ? string.Empty
            : $" {missing} of the usual assemblies were not found, so a little less than the whole standard library is covered.";

        return new CompileReferenceSet(
            references,
            CompileReferenceState.FrameworkOnly,
            "The .NET framework this application runs on, and nothing else. Syntax and standard library mistakes are caught; "
            + "any type the surrounding project defines is not known here and will read as missing."
            + note,
            null);
    }
}
