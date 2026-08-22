namespace LocalNEXUS.Evals;

/// <summary>Which task sets to run.</summary>
[Flags]
public enum TaskSetChoice
{
    /// <summary>Nothing named, which means the Unity set, so an old command line runs as it did.</summary>
    None = 0,

    /// <summary>The Unity set.</summary>
    Unity = 1,

    /// <summary>The plain C# set.</summary>
    Plain = 2
}

/// <summary>
/// The two task sets, and which of them a run is asking for.
/// </summary>
/// <remarks>
/// Selection lives here rather than in either set, so neither has to know the other exists. The
/// Unity set is untouched by any of this: it is asked for its tasks and its version and nothing
/// more, which is what keeps its numbers comparable with every run before this one.
/// </remarks>
public static class TaskSets
{
    /// <summary>The tasks a choice selects, filtered by identifier when any were named.</summary>
    public static IReadOnlyList<EvalTask> Select(TaskSetChoice choice, IReadOnlyCollection<string> ids)
    {
        var all = new List<EvalTask>();

        if (choice.HasFlag(TaskSetChoice.Unity) || choice == TaskSetChoice.None)
        {
            all.AddRange(TaskSet.Tasks);
        }

        if (choice.HasFlag(TaskSetChoice.Plain))
        {
            all.AddRange(PlainTaskSet.Tasks);
        }

        return ids.Count == 0
            ? all
            : all.Where(t => ids.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>Every identifier either set knows, for the message when nothing matched.</summary>
    public static IEnumerable<string> EveryId
        => TaskSet.Tasks.Select(t => t.Id).Concat(PlainTaskSet.Tasks.Select(t => t.Id));

    /// <summary>
    /// What to record as the task set a result belongs to.
    /// </summary>
    /// <remarks>
    /// Derived from the tasks actually run rather than from what was asked for, so a run narrowed
    /// to one task by identifier is still labelled by the set that task came from. A run holding
    /// both is labelled as both, because a mixed run's totals are a mixture and calling it either
    /// one would be a claim about comparability that is not true.
    /// </remarks>
    public static string VersionFor(IReadOnlyList<EvalTask> tasks)
    {
        var unity = tasks.Any(t => t.Project == ProjectShape.Unity);
        var plain = tasks.Any(t => t.Project == ProjectShape.Plain);

        return (unity, plain) switch
        {
            (true, true) => $"{TaskSet.Version}+{PlainTaskSet.Version}",
            (false, true) => PlainTaskSet.Version,
            _ => TaskSet.Version
        };
    }
}
