using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalNEXUS.Evals;

/// <summary>
/// Puts the numbers somewhere they can be compared with last week's.
/// </summary>
/// <remarks>
/// Three shapes, because three different people want them. A markdown summary for reading, the
/// full JSON for anything the summary does not answer, and one row per task appended to a single
/// CSV so a sequence of runs can be looked at as a sequence without opening any of them.
///
/// Every file carries the conditions that produced it. A number without them cannot be compared
/// with anything, and the whole purpose here is comparison.
/// </remarks>
public static class ResultWriter
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>The CSV every run appends to, so a series is readable as a series.</summary>
    public const string HistoryFileName = "history.csv";

    /// <summary>Writes all three and returns the folder they went to.</summary>
    public static string Write(EvalRun run, IReadOnlyList<EvalTask> tasks, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        var stamp = run.Conditions.StartedAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var slug = Slug(run.Conditions.ModelName);

        File.WriteAllText(
            Path.Combine(outputDirectory, $"{stamp}-{slug}.json"),
            JsonSerializer.Serialize(run, Json));

        File.WriteAllText(
            Path.Combine(outputDirectory, $"{stamp}-{slug}.md"),
            Summarise(run, tasks));

        AppendHistory(run, tasks, Path.Combine(outputDirectory, HistoryFileName));

        return outputDirectory;
    }

    /// <summary>The summary, which is meant to be read rather than opened in a spreadsheet.</summary>
    public static string Summarise(EvalRun run, IReadOnlyList<EvalTask> tasks)
    {
        var c = run.Conditions;
        var text = new StringBuilder();

        text.AppendLine($"# Eval run, {c.ModelName}");
        text.AppendLine();
        text.AppendLine($"{c.StartedAt:yyyy-MM-dd HH:mm} on {c.MachineName}. Took {Duration(run.TotalWallTime)}.");
        text.AppendLine();

        text.AppendLine("## Conditions");
        text.AppendLine();
        text.AppendLine("| | |");
        text.AppendLine("|---|---|");
        text.AppendLine($"| Model | {c.ModelName} |");
        text.AppendLine($"| Quantization | {c.Quantization} |");
        text.AppendLine($"| Context size | {c.ContextSize} |");
        text.AppendLine($"| GPU layers | {c.GpuLayers} |");
        text.AppendLine($"| Temperature | {c.Temperature} |");
        text.AppendLine($"| Max tokens | {c.MaxTokens} |");
        text.AppendLine($"| Planner budget | {c.ContextBudget} |");
        text.AppendLine($"| Task set | v{c.TaskSetVersion} |");
        text.AppendLine($"| App version | {c.AppVersion} |");
        text.AppendLine();

        text.AppendLine("## Per task");
        text.AppendLine();
        text.AppendLine("| Task | Met the bar | Plan | Plan landed | Asked for | First pass | Repaired | Never | Repairs | Reused | Dupe tried | Refused by | Tokens out | Time |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");

        foreach (var task in tasks)
        {
            foreach (var result in run.Results.Where(r => r.TaskId == task.Id).OrderBy(r => r.Attempt))
            {
                var label = run.Results.Count(r => r.TaskId == task.Id) > 1
                    ? $"{task.Id} #{result.Attempt}"
                    : task.Id;

                text.AppendLine(
                    $"| {label} "
                    + $"| {(result.MetTheBar(task) ? "yes" : "no")} "
                    + $"| {result.PlannedFiles} "
                    + $"| {result.PlannedFilesLanded} of {result.PlannedFiles} "
                    + $"| {result.ExpectedNewFilesLanded + result.ExpectedEditsLanded} of {task.ExpectedFileCount} "
                    + $"| {result.FilesCompiledFirstPass} "
                    + $"| {result.FilesCompiledAfterRepair} "
                    + $"| {result.FilesNeverCompiled} "
                    + $"| {result.RepairAttempts} "
                    + $"| {Reuse(result, task)} "
                    + $"| {(result.AttemptedDuplicate(task) ? "yes" : "no")} "
                    + $"| {(result.RefusalsFired.Count == 0 ? "nothing" : string.Join("; ", result.RefusalsFired.Select(Rule)))} "
                    + $"| {result.CompletionTokens} "
                    + $"| {Duration(result.WallTime)} |");
            }
        }

        text.AppendLine();
        text.AppendLine("## Totals");
        text.AppendLine();

        var results = run.Results;
        var checkedFiles = results.Sum(r => r.FilesChecked);
        var firstPass = results.Sum(r => r.FilesCompiledFirstPass);
        var everCompiled = results.Sum(r => r.FilesCompiled);
        var met = results.Count(r => MetTheBar(r, tasks));

        text.AppendLine($"- **Tasks met the bar:** {met} of {results.Count} ({Percent(met, results.Count)})");
        text.AppendLine($"- **First pass compile rate:** {firstPass} of {checkedFiles} files ({Percent(firstPass, checkedFiles)})");
        text.AppendLine($"- **Compiled eventually:** {everCompiled} of {checkedFiles} files ({Percent(everCompiled, checkedFiles)})");
        text.AppendLine($"- **Repair attempts used:** {results.Sum(r => r.RepairAttempts)}");
        text.AppendLine($"- **Files left uncompiled:** {results.Sum(r => r.FilesNeverCompiled)}");
        text.AppendLine($"- **Files nothing could be established about:** {results.Sum(r => r.FilesInconclusive)}");
        var reuseTasks = results.Where(r => TaskFor(r, tasks)?.TypeThatShouldBeReused is { Length: > 0 }).ToList();
        var reused = reuseTasks.Count(r => r.ReusedAsIntended(TaskFor(r, tasks)!));
        var attempted = results.Count(r => TaskFor(r, tasks) is { } t && r.AttemptedDuplicate(t));

        var refusalTasks = results.Where(r => TaskFor(r, tasks)?.ExpectsRefusal == true).ToList();
        var refusedRight = refusalTasks.Count(r => r.RefusedByTheRightRule(TaskFor(r, tasks)!));

        text.AppendLine($"- **Reused the existing type when it should have:** {reused} of {reuseTasks.Count}");
        text.AppendLine($"- **Went for a second copy instead:** {attempted} of {results.Count}");
        text.AppendLine($"- **Duplicate types that reached disk:** {results.Sum(r => r.DuplicateTypes.Count)}");
        var quietTasks = results.Where(r => TaskFor(r, tasks)?.ExpectsNoChange == true).ToList();
        var askTasks = results.Where(r => TaskFor(r, tasks)?.ExpectsClarification == true).ToList();

        text.AppendLine($"- **Refused by the rule the task was built to trip:** {refusedRight} of {refusalTasks.Count}");
        text.AppendLine($"- **Left the project alone when that was the answer:** {quietTasks.Count(r => r.LeftEverythingAlone)} of {quietTasks.Count}");
        text.AppendLine($"- **Asked rather than guessed when the request was ambiguous:** {askTasks.Count(r => r.ClarificationsAsked > 0)} of {askTasks.Count}");
        text.AppendLine($"- **Clarifications asked in total:** {results.Sum(r => r.ClarificationsAsked)}");
        text.AppendLine($"- **Guardrail refusals in total:** {results.Sum(r => r.RefusalsFired.Count)}");

        foreach (var group in results
            .SelectMany(r => r.RefusalsFired.Select(Rule))
            .GroupBy(rule => rule, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()))
        {
            text.AppendLine($"  - {group.Key}: {group.Count()}");
        }
        text.AppendLine($"- **Runs that faulted:** {results.Count(r => r.Faulted)}");
        text.AppendLine($"- **Model calls:** {results.Sum(r => r.ModelCalls)}");
        text.AppendLine($"- **Prompt tokens:** {results.Sum(r => r.PromptTokens):n0}");
        text.AppendLine($"- **Completion tokens:** {results.Sum(r => r.CompletionTokens):n0}");
        text.AppendLine($"- **Replies cut off by the token ceiling:** {results.Sum(r => r.TruncatedReplies)}");
        text.AppendLine($"- **Fences left in generated code:** {results.Sum(r => r.FencesLeftInOutput)}");
        text.AppendLine($"- **Meta files lost:** {results.Sum(r => r.ScriptsMissingMeta.Count)}");
        text.AppendLine($"- **Files deleted:** {results.Sum(r => r.DeletedFiles.Count)}");

        var cost = results.Where(r => r.CostUsd is not null).Sum(r => r.CostUsd!.Value);

        text.AppendLine(results.Any(r => r.CostUsd is not null)
            ? $"- **Cost:** {cost:C}"
            : "- **Cost:** nothing was charged, because everything here ran locally.");

        text.AppendLine();
        text.AppendLine("## What went wrong");
        text.AppendLine();

        var problems = results
            .Where(r => !MetTheBar(r, tasks))
            .Select(r => Describe(r, tasks))
            .ToList();

        if (problems.Count == 0)
        {
            text.AppendLine("Nothing. Every task came out the way it was supposed to.");
        }
        else
        {
            foreach (var problem in problems)
            {
                text.AppendLine($"- {problem}");
            }
        }

        text.AppendLine();
        text.AppendLine("## Plans");
        text.AppendLine();
        text.AppendLine("What the planner decided, which is where most of what went wrong began.");
        text.AppendLine();

        foreach (var result in results)
        {
            text.AppendLine($"**{result.TaskId} #{result.Attempt}**");

            if (result.PlanRows.Count == 0)
            {
                text.AppendLine();
                text.AppendLine("- no plan was produced");
            }
            else
            {
                foreach (var row in result.PlanRows)
                {
                    text.AppendLine($"- {row}");
                }
            }

            foreach (var blocked in result.BlockedByDuplicateGuard)
            {
                text.AppendLine($"- refused by the duplicate guard: {blocked}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>The rule name out of a recorded refusal, which reads "Rule on path".</summary>
    private static string Rule(string refusal)
    {
        var at = refusal.IndexOf(" on ", StringComparison.Ordinal);
        return at < 0 ? refusal : refusal[..at];
    }

    /// <summary>How the reuse column reads for a task that has nothing to reuse.</summary>
    private static string Reuse(TaskResult result, EvalTask task)
        => task.TypeThatShouldBeReused is { Length: > 0 }
            ? result.ReusedAsIntended(task) ? "yes" : "no"
            : "n/a";

    private static EvalTask? TaskFor(TaskResult result, IReadOnlyList<EvalTask> tasks)
        => tasks.FirstOrDefault(t => t.Id == result.TaskId);

    private static bool MetTheBar(TaskResult result, IReadOnlyList<EvalTask> tasks)
    {
        var task = tasks.FirstOrDefault(t => t.Id == result.TaskId);
        return task is not null && result.MetTheBar(task);
    }

    private static string Describe(TaskResult result, IReadOnlyList<EvalTask> tasks)
    {
        var task = tasks.First(t => t.Id == result.TaskId);
        var reasons = new List<string>();

        if (result.Faulted)
        {
            reasons.Add($"the run stopped: {result.FaultMessage}");
        }

        if (result.DuplicateTypes.Count > 0)
        {
            reasons.Add($"a duplicate type reached disk ({string.Join("; ", result.DuplicateTypes)})");
        }

        if (task.TypeThatShouldBeReused is { Length: > 0 } wanted && !result.ReusedAsIntended(task))
        {
            reasons.Add(result.BlockedByDuplicateGuard.Count > 0
                ? $"it went for a second {wanted} and the guard stopped it ({string.Join("; ", result.BlockedByDuplicateGuard)})"
                : $"it did not reuse {wanted}");
        }

        if (result.DeletedFiles.Count > 0)
        {
            reasons.Add($"files disappeared ({string.Join(", ", result.DeletedFiles)})");
        }

        if (result.ScriptsMissingMeta.Count > 0)
        {
            reasons.Add($"a meta file was lost ({string.Join(", ", result.ScriptsMissingMeta)})");
        }

        if (task.ExpectsRefusal && !result.RefusedByTheRightRule(task))
        {
            var wantedRules = string.Join(" or ", task.RefusalRules);

            reasons.Add(result.RefusalsFired.Count == 0
                ? $"nothing refused it and {wantedRules} should have"
                : $"it was refused by {string.Join(", ", result.RefusalsFired.Select(Rule))} rather than by {wantedRules}");
        }

        if (task.ExpectsNoChange && !result.LeftEverythingAlone)
        {
            reasons.Add(
                "the project already did what was asked and it wrote anyway ("
                + string.Join(", ", result.UnexpectedNewFiles.Concat(result.ChangedFiles))
                + ")");
        }

        if (task.ExpectsClarification && result.ClarificationsAsked == 0)
        {
            reasons.Add("the request was ambiguous and it guessed rather than asking");
        }

        if (!task.ExpectsRefusal && result.RefusalsFired.Count > 0)
        {
            reasons.Add($"a guardrail refused something ({string.Join("; ", result.RefusalsFired)})");
        }

        if (result.FilesNeverCompiled > 0)
        {
            reasons.Add($"{result.FilesNeverCompiled} file(s) never compiled");
        }

        var landed = result.ExpectedNewFilesLanded + result.ExpectedEditsLanded;

        if (!task.ExpectsRefusal && landed < task.ExpectedFileCount)
        {
            reasons.Add($"only {landed} of {task.ExpectedFileCount} expected file(s) landed");
        }

        if (result.UnexpectedNewFiles.Count > 0)
        {
            reasons.Add($"files nobody asked for ({string.Join(", ", result.UnexpectedNewFiles)})");
        }

        return reasons.Count == 0
            ? $"**{result.TaskId} #{result.Attempt}**: did not meet the bar and nothing here says why, which is itself worth looking at"
            : $"**{result.TaskId} #{result.Attempt}**: {string.Join("; ", reasons)}";
    }

    /// <summary>
    /// One row per task appended to a single file, so a series of runs reads as a series.
    /// </summary>
    /// <remarks>
    /// The conditions are on every row rather than in a header, because rows from different runs
    /// sit next to each other and a row that does not carry its own conditions cannot be sorted,
    /// filtered or compared without going back to the file it came from.
    /// </remarks>
    private static void AppendHistory(EvalRun run, IReadOnlyList<EvalTask> tasks, string path)
    {
        var header = string.Join(",",
            "started_at", "app_version", "task_set", "model", "quantization", "context_size", "gpu_layers",
            "temperature", "max_tokens", "task", "shape", "attempt", "met_the_bar", "faulted", "run_state",
            "planned_files", "planned_landed", "planned_creates", "planned_edits", "reused_as_intended", "attempted_duplicate", "refused_by_expected_rule", "clarifications", "left_alone", "expected_files", "landed_files", "first_pass", "repaired", "never_compiled",
            "inconclusive", "repair_attempts", "duplicates", "refusals", "refusal_expected", "staged",
            "unexpected_files", "fences_left", "model_calls", "prompt_tokens", "completion_tokens",
            "cost_usd", "wall_seconds", "model_seconds", "first_token_seconds", "truncated", "generated_chars");

        var existed = File.Exists(path);
        using var writer = new StreamWriter(path, append: true, Encoding.UTF8);

        if (!existed)
        {
            writer.WriteLine(header);
        }

        var c = run.Conditions;

        foreach (var r in run.Results)
        {
            var task = tasks.First(t => t.Id == r.TaskId);

            writer.WriteLine(string.Join(",",
                Csv(c.StartedAt.ToString("O", CultureInfo.InvariantCulture)),
                Csv(c.AppVersion),
                Csv(c.TaskSetVersion),
                Csv(c.ModelName),
                Csv(c.Quantization),
                c.ContextSize,
                c.GpuLayers,
                Number(c.Temperature),
                c.MaxTokens,
                Csv(r.TaskId),
                Csv(r.Shape.ToString()),
                r.Attempt,
                r.MetTheBar(task) ? 1 : 0,
                r.Faulted ? 1 : 0,
                Csv(r.RunState),
                r.PlannedFiles,
                r.PlannedFilesLanded,
                r.PlannedCreates,
                r.PlannedEdits,
                r.ReusedAsIntended(task) ? 1 : 0,
                r.AttemptedDuplicate(task) ? 1 : 0,
                r.RefusedByTheRightRule(task) ? 1 : 0,
                r.ClarificationsAsked,
                r.LeftEverythingAlone ? 1 : 0,
                task.ExpectedFileCount,
                r.ExpectedNewFilesLanded + r.ExpectedEditsLanded,
                r.FilesCompiledFirstPass,
                r.FilesCompiledAfterRepair,
                r.FilesNeverCompiled,
                r.FilesInconclusive,
                r.RepairAttempts,
                r.DuplicateTypes.Count,
                r.RefusalsFired.Count,
                r.RefusalWasExpected ? 1 : 0,
                r.StagedFiles,
                r.UnexpectedNewFiles.Count,
                r.FencesLeftInOutput,
                r.ModelCalls,
                r.PromptTokens,
                r.CompletionTokens,
                r.CostUsd is { } cost ? Number((double)cost) : string.Empty,
                Number(r.WallTime.TotalSeconds),
                Number(r.ModelTime.TotalSeconds),
                r.TimeToFirstToken is { } first ? Number(first.TotalSeconds) : string.Empty,
                r.TruncatedReplies,
                r.GeneratedCharacters));
        }
    }

    private static string Csv(string value)
        => value.Contains(',') || value.Contains('"')
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Percent(int part, int whole)
        => whole == 0 ? "nothing to measure" : $"{(double)part / whole:P0}";

    private static string Duration(TimeSpan span)
        => span.TotalMinutes >= 1
            ? $"{span.TotalMinutes:0.0} min"
            : $"{span.TotalSeconds:0} s";

    private static string Slug(string name)
        => string.Concat(Path.GetFileNameWithoutExtension(name)
            .Select(ch => char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : '-'))
            .Trim('-');
}
