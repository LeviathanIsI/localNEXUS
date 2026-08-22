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
        string? unityVersion,
        ProjectSourceSet? projectSources = null,
        Microsoft.CodeAnalysis.CSharp.LanguageVersion language = Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp9)
    {
        References = references;
        State = state;
        Summary = summary;
        UnityVersion = unityVersion;
        ProjectSources = projectSources;
        Language = language;
    }

    /// <summary>The assemblies themselves.</summary>
    public IReadOnlyList<MetadataReference> References { get; }

    /// <summary>How complete this set is, which decides how much a passing compile proves.</summary>
    public CompileReferenceState State { get; }

    /// <summary>A sentence naming what was found, shown in the panel and written to the feed.</summary>
    public string Summary { get; }

    /// <summary>The editor version these came from, or null when there was none.</summary>
    public string? UnityVersion { get; }

    /// <summary>
    /// The open project's own source, when it was read, so a check can see the types around it.
    /// </summary>
    /// <remarks>
    /// Not a reference yet, because which of its files have to be left out depends on what is
    /// being written. It becomes one per check.
    ///
    /// Null on the Unity path, which gets the project's types from its compiled assemblies
    /// instead and is not changed by any of this.
    /// </remarks>
    public ProjectSourceSet? ProjectSources { get; }

    /// <summary>
    /// The language version to parse and compile at.
    /// </summary>
    /// <remarks>
    /// C# 9 for Unity, because that is what Unity accepts and compiling at anything newer would
    /// let syntax through here that Unity then rejects. Anywhere else that reasoning does not
    /// apply and holding a modern project to C# 9 would invent errors about file scoped
    /// namespaces and records that its own build is perfectly happy with.
    /// </remarks>
    public Microsoft.CodeAnalysis.CSharp.LanguageVersion Language { get; }

    /// <summary>True when there is enough here to attempt a compile at all.</summary>
    public bool CanCompile => State is CompileReferenceState.Complete
        or CompileReferenceState.ProjectNotCompiled
        or CompileReferenceState.ProjectResolved
        or CompileReferenceState.ProjectNotRestored
        or CompileReferenceState.FrameworkOnly;

    /// <summary>
    /// True when the set is short of things the code being checked may legitimately use.
    /// </summary>
    /// <remarks>
    /// The reason an error can be a phantom. Under a complete set a missing type means the code is
    /// wrong; under a partial one it means the code is wrong or the reference is absent, and those
    /// two are indistinguishable from the diagnostic alone.
    /// </remarks>
    /// <remarks>
    /// Resolved is not partial, and that is the whole of what v1.41 bought. With the project's own
    /// source and its restored packages present, a type that cannot be found is a type that is not
    /// there, which is the difference between a check that proves something and one that reports
    /// it could not tell.
    /// </remarks>
    public bool IsPartial => State is CompileReferenceState.ProjectNotCompiled
        or CompileReferenceState.ProjectNotRestored
        or CompileReferenceState.FrameworkOnly;

    /// <summary>What the node says about what it can reach, before anything has run.</summary>
    public string Reachability => State switch
    {
        CompileReferenceState.Complete => UnityVersion is null ? "Full reference set" : $"Full reference set, Unity {UnityVersion}",
        CompileReferenceState.ProjectNotCompiled => UnityVersion is null ? "Partial reference set" : $"Partial reference set, Unity {UnityVersion}",
        CompileReferenceState.ProjectResolved => "The project's own types and packages",
        CompileReferenceState.ProjectNotRestored => "The project's own types, but it has not been restored",
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
        => new(References, State, $"{note} {Summary}", UnityVersion, ProjectSources, Language);

    /// <summary>An empty set that explains why it is empty.</summary>
    public static CompileReferenceSet Unavailable(CompileReferenceState state, string summary)
        => new(Array.Empty<MetadataReference>(), state, summary, null);
}
