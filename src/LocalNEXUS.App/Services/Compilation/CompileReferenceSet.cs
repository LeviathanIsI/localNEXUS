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
    public bool CanCompile => State is CompileReferenceState.Complete
        or CompileReferenceState.ProjectNotCompiled
        or CompileReferenceState.FrameworkOnly;

    /// <summary>
    /// True when the set is short of things the code being checked may legitimately use.
    /// </summary>
    /// <remarks>
    /// The reason an error can be a phantom. Under a complete set a missing type means the code is
    /// wrong; under a partial one it means the code is wrong or the reference is absent, and those
    /// two are indistinguishable from the diagnostic alone.
    /// </remarks>
    public bool IsPartial => State is CompileReferenceState.ProjectNotCompiled or CompileReferenceState.FrameworkOnly;

    /// <summary>What the node says about what it can reach, before anything has run.</summary>
    public string Reachability => State switch
    {
        CompileReferenceState.Complete => UnityVersion is null ? "Full reference set" : $"Full reference set, Unity {UnityVersion}",
        CompileReferenceState.ProjectNotCompiled => UnityVersion is null ? "Partial reference set" : $"Partial reference set, Unity {UnityVersion}",
        CompileReferenceState.FrameworkOnly => "Framework only, no Unity",
        CompileReferenceState.NoFrameworkReferences => "Nothing to compile against",
        _ => "Not checked yet"
    };

    /// <summary>
    /// The same set, with something said in front of its summary.
    /// </summary>
    /// <remarks>
    /// For the case where a fallback is in force because something better was expected and failed.
    /// The set describes what is actually being compiled against; the note says why it is not what
    /// the user would have assumed.
    /// </remarks>
    public CompileReferenceSet WithNote(string note)
        => new(References, State, $"{note} {Summary}", UnityVersion);

    /// <summary>An empty set that explains why it is empty.</summary>
    public static CompileReferenceSet Unavailable(CompileReferenceState state, string summary)
        => new(Array.Empty<MetadataReference>(), state, summary, null);
}
