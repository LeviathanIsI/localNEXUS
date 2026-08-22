using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Services.Planning;

/// <summary>
/// Refuses to create a type the project already has.
/// </summary>
/// <remarks>
/// This is the anti orphaning rule, and it is enforced here rather than asked of the model on
/// purpose. Left to its own judgement the shortest path for a coder is always a new file, so a
/// project acquires a second inventory beside the one it had, each half wired and neither
/// complete. A rule the index enforces cannot be talked out of.
///
/// What it does when it refuses matters as much as the refusal: it names the existing type and
/// where it lives, so the plan can be turned into an edit of that file or a new file that
/// references it. A refusal that only says no would push the work back to the person.
/// </remarks>
public static class DuplicateTypeGuard
{
    /// <summary>The verdict on one proposed new type.</summary>
    /// <param name="Allowed">False when the project already provides this.</param>
    /// <param name="ExistingPath">Where the existing type lives, when there is one.</param>
    /// <param name="Message">What to say about it, in the feed and to the planner.</param>
    public readonly record struct Verdict(bool Allowed, string? ExistingPath, string Message);

    /// <summary>
    /// One type the guard refused to let a plan create a second copy of.
    /// </summary>
    /// <remarks>
    /// The single failure this application exists to prevent, so it is worth more than a sentence.
    /// Refusals used to be returned as formatted strings, which read well in the feed and meant
    /// that nothing could tell how often the guard had fired, or on what, without reading English.
    /// </remarks>
    /// <param name="TypeName">The type the plan wanted to create.</param>
    /// <param name="PlannedPath">Where it wanted to put it.</param>
    /// <param name="ExistingPath">Where the type already lives, or null when the collision is with an earlier row of this same plan.</param>
    /// <param name="Message">What to say about it.</param>
    public sealed record Refusal(string TypeName, string PlannedPath, string? ExistingPath, string Message)
    {
        public override string ToString() => Message;
    }

    /// <summary>
    /// Whether a new type of this name may be created.
    /// </summary>
    /// <param name="index">The project index, which is the authority on what exists.</param>
    /// <param name="typeName">The type the plan wants to create.</param>
    /// <param name="targetPath">Where the plan wants to put it, relative to the project root.</param>
    /// <param name="alreadyPlanned">Types earlier steps of this same plan will create.</param>
    public static Verdict Check(
        ProjectIndexService index,
        string typeName,
        string targetPath,
        IReadOnlyCollection<string> alreadyPlanned)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (string.IsNullOrWhiteSpace(typeName))
        {
            return new Verdict(true, null, string.Empty);
        }

        if (alreadyPlanned.Contains(typeName, StringComparer.OrdinalIgnoreCase))
        {
            return new Verdict(
                false,
                null,
                $"{typeName} is already being created earlier in this same plan. Fold the work into that file rather than declaring it twice.");
        }

        var existing = index.FindType(typeName);

        if (existing.Count == 0)
        {
            return new Verdict(true, null, string.Empty);
        }

        // A partial type is meant to be spread over several files, so adding another one is the
        // language working as intended rather than a duplicate.
        if (existing.All(t => t.IsPartial))
        {
            return new Verdict(true, null, $"{typeName} exists as a partial type, so another part of it is allowed.");
        }

        var declaration = existing[0];
        var file = index.FileOf(declaration);
        var where = file?.RelativePath ?? "somewhere in this project";

        // Rewriting the file the type already lives in is an edit, not a duplicate, and the plan
        // is allowed to say so.
        if (file is not null && string.Equals(ProjectIndexService.Normalise(targetPath), file.RelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return new Verdict(true, file.RelativePath, $"{typeName} already lives here, so this is an edit rather than a new type.");
        }

        return new Verdict(
            false,
            file?.RelativePath,
            $"{typeName} already exists in {where} as a {Describe(declaration.Kind)}. "
            + "Edit that file, or write something that references it, rather than declaring a second one.");
    }

    /// <summary>
    /// Applies the guard across a whole plan, returning the tasks that survived and what was
    /// refused. Ordering is preserved, so a task allowed only because an earlier one creates its
    /// dependency is judged after that one.
    /// </summary>
    public static (IReadOnlyList<CodeTask> Allowed, IReadOnlyList<Refusal> Blocked) Filter(
        ProjectIndexService index,
        IReadOnlyList<CodeTask> tasks)
    {
        var allowed = new List<CodeTask>();
        var blocked = new List<Refusal>();
        var planned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)
        {
            if (task.Operation == FileOperation.Edit)
            {
                allowed.Add(task);
                planned.Add(task.TypeName);
                continue;
            }

            var verdict = Check(index, task.TypeName, task.RelativePath, planned);

            if (verdict.Allowed)
            {
                allowed.Add(task);
                planned.Add(task.TypeName);
                continue;
            }

            blocked.Add(new Refusal(task.TypeName, task.RelativePath, verdict.ExistingPath, verdict.Message));
        }

        return (allowed, blocked);
    }

    private static string Describe(IndexedTypeKind kind) => kind switch
    {
        IndexedTypeKind.MonoBehaviour => "MonoBehaviour",
        IndexedTypeKind.ScriptableObject => "ScriptableObject",
        IndexedTypeKind.Interface => "interface",
        IndexedTypeKind.Enum => "enum",
        IndexedTypeKind.Struct => "struct",
        IndexedTypeKind.Record => "record",
        _ => "class"
    };
}
