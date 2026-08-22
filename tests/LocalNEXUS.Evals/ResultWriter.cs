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
        var inconclusive = results.Sum(r => r.FilesInconclusive);

        text.AppendLine(
            $"- **Files nothing could be established about:** {inconclusive} of {checkedFiles} "
            + $"({Percent(inconclusive, checkedFiles)})");
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

        AppendPlainSection(text, results, tasks, inconclusive, checkedFiles);

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
    /// <summary>
    /// The two numbers only a plain project can produce, written when the run held one.
    /// </summary>
    /// <remarks>
    /// Kept out of the totals above rather than folded in, because both are meaningless for a
    /// Unity run and a line that reads "0 of 0" in every report is a line nobody reads.
    ///
    /// The first is the one this set was built for. The Unity write rules were scoped in v1.37 and
    /// held there by deterministic tests; this is the only thing that watches them with a real
    /// model writing real files, and the number that should always be nought is the useful kind of
    /// number, because the day it is not is the day something regressed.
    ///
    /// The second is the honest accounting of what could not be measured. Outside Unity the
    /// compile check sees the framework and whatever this run has already settled, and nothing the
    /// project declares, so a file that calls into existing code comes back neither compiled nor
    /// broken. That is a limit of the harness rather than a fact about the model, and the share of
    /// the set it covers is the argument for reading a csproj and loading what it names.
    /// </remarks>
    private static void AppendPlainSection(
        StringBuilder text,
        IReadOnlyList<TaskResult> results,
        IReadOnlyList<EvalTask> tasks,
        int inconclusive,
        int checkedFiles)
    {
        var plain = results.Where(r => TaskFor(r, tasks)?.Project == ProjectShape.Plain).ToList();

        if (plain.Count == 0)
        {
            return;
        }

        var fired = plain.SelectMany(r => r.UnityRefusalsFired).ToList();
        var plainChecked = plain.Sum(r => r.FilesChecked);
        var plainInconclusive = plain.Sum(r => r.FilesInconclusive);
        var plainProven = plain.Sum(r => r.FilesCompiled);
        var allowedRenames = plain.Where(r => TaskFor(r, tasks)?.Shape == TaskShape.AllowedRename).ToList();
        var declaredTwice = plain.Count(r => r.RefusedForDeclaringTwice);

        text.AppendLine();
        text.AppendLine("## On the plain C# project");
        text.AppendLine();

        text.AppendLine(
            $"- **Unity rules that fired, which should be none:** {fired.Count}");

        foreach (var group in fired
            .Select(Rule)
            .GroupBy(rule => rule, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count()))
        {
            text.AppendLine($"  - {group.Key}: {group.Count()}, which is a defect rather than a score");
        }

        text.AppendLine(
            $"- **Edits a Unity project would have refused and this one allowed:** "
            + $"{allowedRenames.Count(r => r.UnityRefusalsFired.Count == 0)} of {allowedRenames.Count}");

        text.AppendLine($"- **Refused for declaring a name twice:** {declaredTwice} of {plain.Count}");

        text.AppendLine(
            $"- **Files the compile check could prove:** {plainProven} of {plainChecked} "
            + $"({Percent(plainProven, plainChecked)})");

        text.AppendLine(
            $"- **Files it could establish nothing about:** {plainInconclusive} of {plainChecked} "
            + $"({Percent(plainInconclusive, plainChecked)})");

        text.AppendLine();
        text.AppendLine(
            "Since v1.41 the check reads the project's own source and, when a restore has left one, the");
        text.AppendLine(
            "packages its restore record names, so a file calling into existing code resolves. What is");
        text.AppendLine(
            "left is the project that has never been restored: its own types are known and its packages");
        text.AppendLine(
            "are not, so the reference set is still short of something and an error blaming a missing");
        text.AppendLine(
            "type is still not trusted. That is the state this scratch project is in, deliberately, since");
        text.AppendLine(
            "restoring it to make the number look better would be measuring something else.");
        text.AppendLine();
        text.AppendLine(
            "So a genuinely wrong reference reads as inconclusive here rather than as an error, which is");
        text.AppendLine(
            "the honest floor rather than a gap: nothing is claimed that was not established.");

        if (checkedFiles != plainChecked || inconclusive != plainInconclusive)
        {
            text.AppendLine();
            text.AppendLine(
                "The totals above cover both sets. These figures cover the plain tasks only.");
        }
    }

    private static void AppendHistory(EvalRun run, IReadOnlyList<EvalTask> tasks, string path)
    {
        var header = string.Join(",",
            "started_at", "app_version", "task_set", "model", "quantization", "context_size", "gpu_layers",
            "temperature", "max_tokens", "task", "shape", "attempt", "met_the_bar", "faulted", "run_state",
            "planned_files", "planned_landed", "planned_creates", "planned_edits", "reused_as_intended", "attempted_duplicate", "refused_by_expected_rule", "clarifications", "left_alone", "expected_files", "landed_files", "first_pass", "repaired", "never_compiled",
            "inconclusive", "repair_attempts", "duplicates", "refusals", "refusal_expected", "staged",
            "unexpected_files", "fences_left", "model_calls", "prompt_tokens", "completion_tokens",
            "cost_usd", "wall_seconds", "model_seconds", "first_token_seconds", "truncated", "generated_chars");

        // A header that no longer describes the rows is worse than no history at all, because
        // every column after the first added one silently shifts and the file still reads.
        // Columns get added whenever a new thing becomes measurable, so this is not a rare case:
        // it happened the first time it could, and the numbers it produced looked plausible.
        //
        // The old file is kept rather than dropped. It is somebody's record of earlier runs and it
        // is still correct against its own header; it just cannot be appended to any more.
        var existed = File.Exists(path);

        if (existed && !string.Equals(File.ReadLines(path).FirstOrDefault(), header, StringComparison.Ordinal))
        {
            var retired = Path.Combine(
                Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}-until-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.csv");

            File.Move(path, retired);
            existed = false;
        }

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
