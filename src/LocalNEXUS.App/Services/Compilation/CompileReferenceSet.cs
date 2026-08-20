using Microsoft.CodeAnalysis;

namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// The assemblies a check compiles against, and how complete they are.
/// </summary>
/// <remarks>
/// Built once and reused, because loading three hundred assemblies costs more than the compile
/// itself and a repair loop runs several compiles in a row.
/// </remarks>
public sealed class CompileReferenceSet
{
    public CompileReferenceSet(
        IReadOnlyList<MetadataReference> references,
        CompileReferenceState state,
        string summary,
        string? unityVersion)
    {
        References = references;
        State = state;
        Summary = summary;
        UnityVersion = unityVersion;
    }

    /// <summary>The assemblies themselves.</summary>
    public IReadOnlyList<MetadataReference> References { get; }

    /// <summary>How complete this set is, which decides how much a passing compile proves.</summary>
    public CompileReferenceState State { get; }

    /// <summary>A sentence naming what was found, shown in the panel and written to the feed.</summary>
    public string Summary { get; }

    /// <summary>The editor version these came from, or null when there was none.</summary>
    public string? UnityVersion { get; }

    /// <summary>True when there is enough here to attempt a compile at all.</summary>
    public bool CanCompile => State is CompileReferenceState.Complete or CompileReferenceState.ProjectNotCompiled;

    /// <summary>An empty set that explains why it is empty.</summary>
    public static CompileReferenceSet Unavailable(CompileReferenceState state, string summary)
        => new(Array.Empty<MetadataReference>(), state, summary, null);
}
