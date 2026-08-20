using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Services.Planning;

/// <summary>Whether a planned file is being written for the first time or changed.</summary>
public enum FileOperation
{
    /// <summary>A file that does not exist yet.</summary>
    Create,

    /// <summary>A file that exists and is being changed.</summary>
    Edit
}

/// <summary>
/// One file the plan says to write, and everything the coder needs to write it.
/// </summary>
/// <remarks>
/// This is the item that travels along a wire. A plan is a list of them, and a wire carrying a
/// list is what makes the coder run once per file without the graph changing shape.
///
/// It carries its own context rather than pointing at somewhere to look it up, so that a coder
/// node can be run against one of these without knowing anything about the planner.
/// </remarks>
public sealed class CodeTask
{
    public CodeTask(
        int order,
        string relativePath,
        string typeName,
        FileOperation operation,
        string intent,
        string projectContext,
        string? existingContent)
    {
        Order = order;
        RelativePath = relativePath;
        TypeName = typeName;
        Operation = operation;
        Intent = intent;
        ProjectContext = projectContext;
        ExistingContent = existingContent;
    }

    /// <summary>Position in the plan, counting from one. Dependencies come first.</summary>
    public int Order { get; }

    /// <summary>Where the file goes, relative to the project root, with forward slashes.</summary>
    public string RelativePath { get; }

    /// <summary>The main type this file declares, which for a MonoBehaviour has to match the file name.</summary>
    public string TypeName { get; }

    /// <summary>Whether this creates a file or changes one.</summary>
    public FileOperation Operation { get; }

    /// <summary>What this file is for, in the planner's words.</summary>
    public string Intent { get; }

    /// <summary>The part of the project this file needs to know about, already fitted to the budget.</summary>
    public string ProjectContext { get; }

    /// <summary>The current contents of the file, when it is being edited. Null for a new file.</summary>
    public string? ExistingContent { get; }

    /// <summary>The file name on its own.</summary>
    public string FileName => System.IO.Path.GetFileName(RelativePath);

    /// <summary>One line for the feed and for the plan preview.</summary>
    public override string ToString()
        => $"{Order}. {(Operation == FileOperation.Create ? "create" : "edit")} {RelativePath}: {Intent}";
}

/// <summary>
/// An ordered set of files to write, with the decisions that produced it.
/// </summary>
/// <remarks>
/// The order is a dependency order: interfaces and data types first, then the things that
/// implement them, then the things that use those. It matters because each step is shown the
/// signatures the earlier steps produced, and a step that runs before the thing it depends on has
/// nothing to be shown.
/// </remarks>
public sealed class FilePlan
{
    public FilePlan(
        IReadOnlyList<CodeTask> tasks,
        IReadOnlyList<CandidateVerdict> verdicts,
        IReadOnlyList<string> blocked,
        string summary)
    {
        Tasks = tasks;
        Verdicts = verdicts;
        Blocked = blocked;
        Summary = summary;
    }

    /// <summary>The files to write, in the order they are to be written.</summary>
    public IReadOnlyList<CodeTask> Tasks { get; }

    /// <summary>What was decided about each existing file that looked relevant.</summary>
    public IReadOnlyList<CandidateVerdict> Verdicts { get; }

    /// <summary>Files the plan asked for that were refused, and why.</summary>
    public IReadOnlyList<string> Blocked { get; }

    /// <summary>One line describing the plan, for the node footer.</summary>
    public string Summary { get; }

    /// <summary>An empty plan, for a request that needs nothing written.</summary>
    public static FilePlan Empty { get; } = new(
        Array.Empty<CodeTask>(),
        Array.Empty<CandidateVerdict>(),
        Array.Empty<string>(),
        "Nothing to write.");

    /// <summary>The plan as text, which is what a wire carries when something reads it as text.</summary>
    public override string ToString()
        => Tasks.Count == 0 ? Summary : string.Join(Environment.NewLine, Tasks.Select(t => t.ToString()));
}

/// <summary>
/// A file the coder produced, on its way to the compile check and then to disk.
/// </summary>
/// <param name="Task">What it was asked to be.</param>
/// <param name="Content">The whole file, after any edit has been applied.</param>
/// <param name="Types">What it declares, parsed back out so later steps can be shown it.</param>
public sealed record GeneratedFile(CodeTask Task, string Content, IReadOnlyList<IndexedType> Types)
{
    /// <summary>Where it goes, relative to the project root.</summary>
    public string RelativePath => Task.RelativePath;

    /// <summary>Whether it creates or changes a file.</summary>
    public FileOperation Operation => Task.Operation;

    /// <summary>The generated code, which is what a wire carries when something reads it as text.</summary>
    public override string ToString() => Content;
}
