namespace LocalNEXUS.Evals;

/// <summary>
/// The shape of work a task represents, so results can be read by category.
/// </summary>
/// <remarks>
/// A model that is fine at writing one file from nothing and hopeless at editing an existing one
/// is a useful thing to know, and an average across everything hides it.
/// </remarks>
public enum TaskShape
{
    /// <summary>One new file that depends on nothing already in the project.</summary>
    NewFileAlone,

    /// <summary>One new file that has to reference a type the project already has.</summary>
    NewFileReferencingExisting,

    /// <summary>A change to a file that is already there.</summary>
    EditExisting,

    /// <summary>Several files where one depends on another written in the same run.</summary>
    MultiFileOrdered,

    /// <summary>One interface and more than one thing implementing it.</summary>
    InterfaceWithImplementations,

    /// <summary>Work on an asset type rather than a component.</summary>
    ScriptableObject,

    /// <summary>The project already does what was asked, so the right answer is to leave it alone.</summary>
    ChangeNothing,

    /// <summary>Not enough was said to act on, so the right answer is to ask.</summary>
    Ambiguous,

    /// <summary>More than one file the project already has has to change.</summary>
    EditTwoFiles,

    /// <summary>More work than the prompt budget can carry context for.</summary>
    OversizedPlan,

    /// <summary>Something named in the request exists nowhere, so it has to be written too.</summary>
    MissingDependency,

    /// <summary>Ordinary single file work, for volume.</summary>
    Routine,

    /// <summary>A request whose right answer is an edit, phrased the way somebody would phrase it.</summary>
    ShouldEditNotCreate,

    /// <summary>A change that compiles cleanly and would silently break a scene.</summary>
    UnityRefusal
}

/// <summary>
/// One file the scratch project starts with.
/// </summary>
/// <param name="RelativePath">Where it goes, relative to the project root.</param>
/// <param name="Content">What it contains.</param>
public sealed record SeedFile(string RelativePath, string Content);

/// <summary>
/// One task: a project to start from, something to ask for, and what would count as having done it.
/// </summary>
/// <remarks>
/// The expectations are deliberately narrow and mechanical. Nothing here judges whether the code is
/// good, because nothing here can: what it judges is whether the file that was asked for exists,
/// whether it compiled, whether a second copy of an existing type appeared, and whether a refusal
/// that should have fired did. Those are the things a number can honestly be put on.
/// </remarks>
/// <param name="Id">Stable identifier, used as the column name in results. Never renamed.</param>
/// <param name="Shape">Which category of work this is.</param>
/// <param name="Request">What is typed into the chat box.</param>
/// <param name="Seed">The project this starts from.</param>
/// <param name="ExpectedNewFiles">File names that should appear that were not there before.</param>
/// <param name="ExpectedEditedFiles">File names that should have changed.</param>
/// <param name="ExpectsRefusal">True when a guardrail should refuse the write.</param>
/// <param name="TypesThatMustNotBeDuplicated">
/// Types the project already declares. A second declaration of any of them anywhere is the
/// failure this application exists to prevent.
/// </param>
/// <param name="TypeThatShouldBeReused">
/// The type already in the project that the right answer changes rather than duplicates, when
/// there is one.
/// </param>
/// <param name="AcceptableRefusalRules">
/// Which project rules would be a correct refusal of this write, named exactly. More than one is
/// allowed because some requests can legitimately be refused by either of two rules depending on
/// how the model went about it, and a refusal by anything outside this list is a different event
/// that does not count as the one the task was built to trip.
/// </param>
/// <param name="ExpectsNoChange">
/// True when the project already does what was asked and the right answer is to write nothing at
/// all. A model that writes something anyway has failed, however good the something is.
/// </param>
/// <param name="ExpectsClarification">
/// True when the request is too underspecified to act on and the right answer is to ask rather
/// than to guess.
/// </param>
public sealed record EvalTask(
    string Id,
    TaskShape Shape,
    string Request,
    IReadOnlyList<SeedFile> Seed,
    IReadOnlyList<string> ExpectedNewFiles,
    IReadOnlyList<string> ExpectedEditedFiles,
    bool ExpectsRefusal,
    IReadOnlyList<string> TypesThatMustNotBeDuplicated,
    string? TypeThatShouldBeReused = null,
    IReadOnlyList<string>? AcceptableRefusalRules = null,
    bool ExpectsNoChange = false,
    bool ExpectsClarification = false)
{
    /// <summary>How many files the task expects to be touched at all.</summary>
    public int ExpectedFileCount => ExpectedNewFiles.Count + ExpectedEditedFiles.Count;

    /// <summary>The rules that would be a correct refusal, never null.</summary>
    public IReadOnlyList<string> RefusalRules => AcceptableRefusalRules ?? Array.Empty<string>();

    public override string ToString() => $"{Id} ({Shape})";
}
