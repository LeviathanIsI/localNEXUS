namespace LocalNEXUS.Evals;

/// <summary>
/// What produced a set of numbers.
/// </summary>
/// <remarks>
/// Written into every result file and reprinted at the top of every summary. A score without its
/// conditions is not a measurement, it is a rumour: the same task set against the same application
/// will answer differently on a different quantization or a different context budget, and six
/// months from now nobody remembers which was which.
/// </remarks>
/// <param name="ModelName">The model file, as it is named on disk.</param>
/// <param name="ModelPath">Where it was loaded from.</param>
/// <param name="Quantization">Read out of the file name, which is the only place it is stated.</param>
/// <param name="ContextSize">Tokens the server was started with.</param>
/// <param name="GpuLayers">How much was offloaded, which decides speed more than anything else here.</param>
/// <param name="Temperature">What the coder was set to.</param>
/// <param name="MaxTokens">The ceiling on one reply.</param>
/// <param name="ContextBudget">What the planner was allowed to put in the prompt.</param>
/// <param name="TaskSetVersion">Which task set. Results across versions are not comparable.</param>
/// <param name="AppVersion">The application these numbers describe.</param>
/// <param name="MachineName">Which machine, because wall time is meaningless without it.</param>
/// <param name="StartedAt">When the run began.</param>
public sealed record RunConditions(
    string ModelName,
    string ModelPath,
    string Quantization,
    int ContextSize,
    int GpuLayers,
    double Temperature,
    int MaxTokens,
    string ContextBudget,
    string TaskSetVersion,
    string AppVersion,
    string MachineName,
    DateTimeOffset StartedAt);

/// <summary>
/// One task, run once.
/// </summary>
/// <remarks>
/// Every field is either counted or read off disk. Nothing here is a judgement about whether the
/// code was any good, because nothing in a harness can make one, and a number that looked like a
/// quality score while measuring file counts would be worse than no number.
/// </remarks>
/// <param name="TaskId">Which task.</param>
/// <param name="Shape">Which category of work.</param>
/// <param name="Attempt">Which repeat, from one.</param>
/// <param name="Faulted">True when the run stopped rather than finishing.</param>
/// <param name="FaultMessage">Why it stopped.</param>
/// <param name="RunState">What the executor called the outcome.</param>
/// <param name="PlannedFiles">How many files the planner decided on.</param>
/// <param name="PlanRows">The plan itself, one line per file, so a bad plan can be read afterwards.</param>
/// <param name="PlannedFilesLanded">
/// How many of the planner's own rows ended up on disk. Separate from whether the task got what it
/// asked for: a plan that was fully carried out and was the wrong plan scores full marks here and
/// nothing on the expectations, which is exactly the distinction worth being able to see.
/// </param>
/// <param name="FilesCompiledFirstPass">Generated files that compiled with no repair.</param>
/// <param name="FilesCompiledAfterRepair">Generated files that needed at least one repair and then compiled.</param>
/// <param name="FilesNeverCompiled">Generated files that were still broken when the retries ran out.</param>
/// <param name="FilesInconclusive">Files whose errors were all missing references, so nothing was established.</param>
/// <param name="RepairAttempts">How many times the coder was asked to fix something.</param>
/// <param name="ExpectedNewFilesLanded">How many of the expected new files are on disk.</param>
/// <param name="ExpectedEditsLanded">How many of the expected edits actually changed the file.</param>
/// <param name="UnexpectedNewFiles">New files nobody asked for.</param>
/// <param name="DeletedFiles">Files that were there before and are not now. Should always be empty.</param>
/// <param name="ScriptsMissingMeta">Scripts whose meta sibling went missing. Should always be empty.</param>
/// <param name="DuplicateTypes">Types now declared in more than one file. The failure that matters most.</param>
/// <param name="BlockedByDuplicateGuard">What the guard refused during planning, one entry per type.</param>
/// <param name="PlannedCreates">Rows the planner chose to write from nothing.</param>
/// <param name="PlannedEdits">Rows the planner chose to change something the project already had.</param>
/// <param name="ReusedTypes">The existing types those edits reuse, named.</param>
/// <param name="CandidateVerdicts">
/// What triage decided about each existing file it considered, as a decision rather than a
/// sentence. This is where a plan that looked straight past an existing type becomes visible.
/// </param>
/// <param name="RefusalsFired">Writes a project rule refused, each naming the rule that fired.</param>
/// <param name="RefusalWasExpected">Whether this task was one where a refusal is the right answer.</param>
/// <param name="StagedFiles">Files kept back rather than written, for any reason.</param>
/// <param name="FencesLeftInOutput">Files still carrying a markdown fence. Should always be zero.</param>
/// <param name="ModelCalls">How many requests reached the model.</param>
/// <param name="PromptTokens">Summed across calls, where the server reported them.</param>
/// <param name="CompletionTokens">Summed across calls, where the server reported them.</param>
/// <param name="CostUsd">What it cost, or null for a local model where nothing is charged.</param>
/// <param name="WallTime">Start to finish, including bringing the model up the first time.</param>
/// <param name="ModelTime">How much of that was spent waiting on the model.</param>
/// <param name="TimeToFirstToken">The first call's wait before anything came back.</param>
/// <param name="TruncatedReplies">Replies that stopped because they hit the token ceiling.</param>
/// <param name="GeneratedCharacters">How much code came out.</param>
/// <param name="ClarificationsAsked">
/// How many times planning stopped to ask something rather than guessing at it.
/// </param>
/// <param name="ChangedFiles">Files the project already had whose contents are not what they were.</param>
/// <param name="ChangedFileContents">
/// What every file the run touched looks like now.
/// </param>
public sealed record TaskResult(
    string TaskId,
    TaskShape Shape,
    int Attempt,
    bool Faulted,
    string? FaultMessage,
    string RunState,
    int PlannedFiles,
    IReadOnlyList<string> PlanRows,
    int PlannedFilesLanded,
    int FilesCompiledFirstPass,
    int FilesCompiledAfterRepair,
    int FilesNeverCompiled,
    int FilesInconclusive,
    int RepairAttempts,
    int ExpectedNewFilesLanded,
    int ExpectedEditsLanded,
    IReadOnlyList<string> UnexpectedNewFiles,
    IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<string> ScriptsMissingMeta,
    IReadOnlyList<string> DuplicateTypes,
    IReadOnlyList<string> BlockedByDuplicateGuard,
    int PlannedCreates,
    int PlannedEdits,
    IReadOnlyList<string> ReusedTypes,
    IReadOnlyList<string> CandidateVerdicts,
    IReadOnlyList<string> RefusalsFired,
    bool RefusalWasExpected,
    int StagedFiles,
    int FencesLeftInOutput,
    int ModelCalls,
    int PromptTokens,
    int CompletionTokens,
    decimal? CostUsd,
    TimeSpan WallTime,
    TimeSpan ModelTime,
    TimeSpan? TimeToFirstToken,
    int TruncatedReplies,
    int GeneratedCharacters,
    int ClarificationsAsked,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyDictionary<string, string> ChangedFileContents)
{
    /// <summary>Files the check reached a verdict on.</summary>
    public int FilesChecked => FilesCompiledFirstPass + FilesCompiledAfterRepair + FilesNeverCompiled + FilesInconclusive;

    /// <summary>Files that compiled in the end, however many attempts it took.</summary>
    public int FilesCompiled => FilesCompiledFirstPass + FilesCompiledAfterRepair;

    /// <summary>
    /// The existing type this task was supposed to reuse was reused.
    /// </summary>
    /// <remarks>
    /// Read from the plan rather than from the disk, because that is where the decision is. A
    /// plan that edited the type the project already had did the right thing whether or not the
    /// edit then compiled, and those are two separate things to be bad at.
    /// </remarks>
    public bool ReusedAsIntended(EvalTask task)
        => task.TypeThatShouldBeReused is { Length: > 0 } wanted
           && ReusedTypes.Any(t => t.StartsWith(wanted, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The planner went for a second copy of something the project already had.
    /// </summary>
    /// <remarks>
    /// The failure this application exists to prevent, counted at the point it is decided rather
    /// than at the point it would have landed. Counting it on disk gave zero every time, because
    /// the write guard stops it reaching disk, and a prevented attempt is still an attempt: it is
    /// the planner being wrong and the guard covering for it, which is worth knowing separately
    /// from the planner being right.
    /// </remarks>
    public bool AttemptedDuplicate(EvalTask task)
    {
        if (task.TypeThatShouldBeReused is not { Length: > 0 } wanted)
        {
            return DuplicateTypes.Count > 0;
        }

        if (ReusedAsIntended(task))
        {
            return false;
        }

        var shortName = wanted[(wanted.LastIndexOf('.') + 1)..];

        // Either the guard caught it, or nothing was reused and something new was written anyway.
        return BlockedByDuplicateGuard.Any(b => b.Contains(shortName, StringComparison.OrdinalIgnoreCase))
               || PlannedCreates > 0
               || DuplicateTypes.Count > 0;
    }

    /// <summary>
    /// The refusal that fired is the one this task was designed to trigger.
    /// </summary>
    /// <remarks>
    /// A refusal by some other rule is a different event. The task that renames a serialized field
    /// is refused for that, and being refused because the planner tried to create a file that
    /// already existed would be the harness scoring a point for the wrong reason.
    /// </remarks>
    public bool RefusedByTheRightRule(EvalTask task)
        => task.RefusalRules.Count > 0
           && task.RefusalRules.Any(rule => RefusalsFired.Any(r => r.StartsWith(rule, StringComparison.Ordinal)));

    /// <summary>
    /// The run left the project exactly as it found it.
    /// </summary>
    /// <remarks>
    /// Only meaningful for a task whose right answer is to do nothing, and there it is the whole
    /// of the answer. A model that produced something good is still wrong.
    /// </remarks>
    public bool LeftEverythingAlone => UnexpectedNewFiles.Count == 0 && ChangedFiles.Count == 0;

    /// <summary>
    /// Whether the task came out the way it was supposed to.
    /// </summary>
    /// <remarks>
    /// Deliberately not a score. It is one line in a table saying whether this particular attempt
    /// did the thing, and it is reported alongside every number that went into it so a reader can
    /// disagree with the definition and use the parts.
    /// </remarks>
    public bool MetTheBar(EvalTask task)
    {
        if (DuplicateTypes.Count > 0 || DeletedFiles.Count > 0 || ScriptsMissingMeta.Count > 0)
        {
            return false;
        }

        if (task.ExpectsNoChange)
        {
            return !Faulted && LeftEverythingAlone;
        }

        if (task.ExpectsClarification)
        {
            // Asking is the pass. What it went on to do afterwards is not what this measures,
            // because with nobody present to answer, proceeding on a stated assumption is the
            // designed behaviour rather than a failure.
            return ClarificationsAsked > 0;
        }

        if (task.ExpectsRefusal)
        {
            // Landing the change is the failure here, not the success, and it has to be the rule
            // this task was built to trip rather than any refusal at all.
            return RefusedByTheRightRule(task) && ExpectedEditsLanded == 0;
        }

        if (AttemptedDuplicate(task))
        {
            return false;
        }

        return !Faulted
               && RefusalsFired.Count == 0
               && FilesNeverCompiled == 0
               && ExpectedNewFilesLanded == task.ExpectedNewFiles.Count
               && ExpectedEditsLanded == task.ExpectedEditedFiles.Count;
    }
}

/// <summary>Everything one invocation of the harness produced.</summary>
/// <param name="Conditions">What produced it.</param>
/// <param name="Results">Every task, every repeat.</param>
/// <param name="TotalWallTime">How long the whole thing took.</param>
public sealed record EvalRun(
    RunConditions Conditions,
    IReadOnlyList<TaskResult> Results,
    TimeSpan TotalWallTime);
