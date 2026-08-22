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
public sealed record EvalTask(
    string Id,
    TaskShape Shape,
    string Request,
    IReadOnlyList<SeedFile> Seed,
    IReadOnlyList<string> ExpectedNewFiles,
    IReadOnlyList<string> ExpectedEditedFiles,
    bool ExpectsRefusal,
    IReadOnlyList<string> TypesThatMustNotBeDuplicated)
{
    /// <summary>How many files the task expects to be touched at all.</summary>
    public int ExpectedFileCount => ExpectedNewFiles.Count + ExpectedEditedFiles.Count;

    public override string ToString() => $"{Id} ({Shape})";
}
