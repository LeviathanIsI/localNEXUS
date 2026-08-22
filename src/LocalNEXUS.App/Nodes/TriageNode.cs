using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.History;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Works out which files a request needs, by looking at what the project already contains.
/// </summary>
/// <remarks>
/// Three steps, in order. The project is indexed, which is cheap after the first time. The
/// request is ranked against that index to find the files it is probably about, and only those
/// are read. Then a model is shown the map, the candidates and the request, and asked what to do
/// about each candidate and which files to write.
///
/// The answer is not taken on trust. A plan that says to create a type the project already has is
/// refused by the index rather than by the model's judgement, because left to itself the shortest
/// path for a coder is always a new file, and that is exactly how a project ends up with a second
/// half wired copy of something it already had.
///
/// It emits the plan as a list of tasks. A wire carrying a list is what makes the coder downstream
/// run once per file without the graph changing shape.
/// </remarks>
public sealed partial class TriageNode : NodeBase
{
    /// <summary>How many candidates ranking offers before any file is read.</summary>
    public const int DefaultCandidateLimit = 12;

    /// <summary>The most files one request is allowed to plan, so a runaway plan cannot land.</summary>
    public const int MaximumFiles = 24;

    /// <summary>How many candidates ranking offers.</summary>
    [ObservableProperty]
    private int _candidateLimit = DefaultCandidateLimit;

    /// <summary>Characters of project map the prompt may carry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BudgetSummary))]
    private int _mapCharacters = 4000;

    /// <summary>Characters of candidate file detail the prompt may carry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BudgetSummary))]
    private int _candidateCharacters = 16000;

    /// <summary>Characters of signatures from earlier files in this run the prompt may carry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BudgetSummary))]
    private int _emittedCharacters = 4000;

    /// <summary>What the last plan decided, one line per file, for the panel.</summary>
    [ObservableProperty]
    private string _lastPlan = string.Empty;

    /// <summary>What the last run decided about each existing file it considered.</summary>
    [ObservableProperty]
    private string _lastDecisions = string.Empty;

    /// <summary>Anything the duplicate guard refused, and why.</summary>
    [ObservableProperty]
    private string _lastBlocked = string.Empty;

    public TriageNode()
        : base("Triage")
    {
        Request = AddInput("Text", PinType.Text);

        // Appended after the request, never before it. A saved graph matches its pins by name and
        // falls back to position, so putting this first would hand it the request's saved identity
        // and drop the wire feeding it.
        Model = AddInput("Model", PinType.Model);
        Plan = AddOutput("Text", PinType.Text);
    }

    /// <summary>Receives the request to plan.</summary>
    public Pin Request { get; }

    /// <summary>The model this plans with, handed in rather than hunted for.</summary>
    public Pin Model { get; }

    /// <summary>Carries the ordered file plan onwards, one item per file to write.</summary>
    public Pin Plan { get; }

    /// <inheritdoc />
    public override string TypeKey => "Triage";

    /// <summary>The budget in force, said out loud so it is never a hidden number.</summary>
    public string BudgetSummary => Budget.Summary;

    /// <summary>The budget these settings describe.</summary>
    public ContextBudget Budget => new()
    {
        MapCharacters = Math.Max(0, MapCharacters),
        CandidateCharacters = Math.Max(0, CandidateCharacters),
        EmittedSignatureCharacters = Math.Max(0, EmittedCharacters),
        CandidateLimit = Math.Clamp(CandidateLimit, 1, 64)
    };

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var request = ctx.GetText(Request);

        if (string.IsNullOrWhiteSpace(request))
        {
            request = ctx.UserRequest;
        }

        if (string.IsNullOrWhiteSpace(request))
        {
            throw new InvalidOperationException($"{Title} received no request to plan.");
        }

        var project = ctx.Services.UnityProject;

        if (!project.HasProject)
        {
            throw new InvalidOperationException(
                $"{Title} needs an open Unity project to know what already exists. Open one from the File menu.");
        }

        var budget = Budget;
        ctx.Feed.Info($"{Title}: context budget", budget.Summary);

        var index = ctx.Services.ProjectIndex;
        var progress = new DelegateProgress<string>(message => StatusMessage = message);

        await index.EnsureAsync(project.ProjectPath, progress, ct).ConfigureAwait(false);
        ctx.Feed.Info($"{Title}: project index", index.StatusText);

        var candidates = RelevanceRanker.Rank(index, request, budget.CandidateLimit);

        ctx.Feed.Info(
            $"{Title}: {candidates.Count} candidate file(s)",
            candidates.Count == 0
                ? "Nothing in the project looked related, so this plans from scratch."
                : string.Join(Environment.NewLine, candidates.Select(c => $"{c.File.RelativePath}  ({c.Reason})")));

        var map = ProjectDigest.BuildMap(index, candidates, budget);
        var summary = ProjectDigest.BuildCandidateSummary(candidates, budget);

        // The planner is handed in on a pin rather than found by searching downstream. It still
        // borrows a model rather than carrying a second copy of every model setting; what changed
        // is that the canvas now says which one, instead of it being whichever node happened to be
        // wired after this one.
        // Read from the wire rather than from what the wire carried. A model handed over for
        // reference may legitimately run after the node using it, which is exactly this graph: the
        // plan goes to the model and the model is what planned it. A reference does not need its
        // producer to have run, only to exist.
        var planner = ctx.GetSourceNode(Model) as IModelHandle;

        if (planner is null)
        {
            throw new InvalidOperationException(
                $"{Title} has no model to plan with. Wire a Model node's Model output into its Model input.");
        }

        if (!planner.CanAnswer(out var whyNot))
        {
            throw new InvalidOperationException($"{Title} cannot plan: {whyNot}");
        }

        var plannerNode = planner as NodeBase
            ?? throw new InvalidOperationException($"{Title} found something that is not a node to plan with.");

        ctx.Feed.Info(
            $"{Title}: planning with {plannerNode.Title}",
            "The model that writes the files is the one that plans them.");

        var message = PlanPrompt.BuildPlannerMessage(request, map, summary, budget);

        var reply = await planner
            .AnswerAsync(PlanPrompt.PlannerSystemPrompt, message, ctx.ForNode(plannerNode), ct)
            .ConfigureAwait(false);

        // One round, and only one. A node that keeps asking is worse than one that guesses,
        // because guessing at least finishes. Whatever comes back from here is planned with.
        var questions = ClarificationParser.Parse(reply);

        if (questions.Count > 0)
        {
            reply = await ResolveAsync(ctx, planner, plannerNode, message, questions, ct).ConfigureAwait(false);
        }

        var parsed = PlanParser.Parse(reply);
        var plan = BuildPlan(ctx, index, project.ProjectPath!, parsed, map, summary, budget);

        Report(ctx, plan);

        if (plan.Tasks.Count == 0)
        {
            // Three quite different things end here and they used to read the same. Nothing
            // parsed at all is a reply in the wrong shape; rows that parsed and were all refused
            // is the guard doing its job; and a plan of nothing is the model declining. Saying
            // which turns a dead end into something somebody can act on.
            var why = parsed.Rows.Count == 0
                ? "Nothing in the reply parsed as a plan row. Each row needs five columns separated by pipes."
                : plan.Blocked.Count > 0
                    ? $"All {parsed.Rows.Count} planned row(s) were refused: "
                      + string.Join("; ", plan.Blocked.Select(b => b.Message))
                    : $"{parsed.Rows.Count} row(s) parsed and none survived.";

            throw new InvalidOperationException(
                $"{Title} produced no files to write. {why}{Environment.NewLine}{Environment.NewLine}"
                + $"The planner replied:{Environment.NewLine}{reply}");
        }

        StatusMessage = plan.Summary;
        return NodeResult.FromPin(Plan, plan.Tasks);
    }

    /// <summary>
    /// Puts the planner's questions to the person and plans again with whatever comes back.
    /// </summary>
    /// <remarks>
    /// The run pauses here, in the sense that this task is awaiting one the interface completes.
    /// It is the same shape as asking for a confirmation, which this application has done since
    /// v0.4, and it is why the executor needs no part in it: nothing above this node knows that a
    /// node can want something, only that a node has not returned yet. Resuming is not a concept
    /// that has to exist, because nothing stopped.
    ///
    /// Nobody answering is a valid outcome rather than a failure. The run proceeds on the first
    /// option of each question and says so, in the chat and in the feed, because an assumption
    /// that cannot be seen is exactly the confidently wrong answer this was meant to prevent.
    /// </remarks>
    private async Task<string> ResolveAsync(
        NodeExecutionContext ctx,
        IModelHandle planner,
        NodeBase plannerNode,
        string originalMessage,
        IReadOnlyList<ClarificationQuestion> questions,
        CancellationToken ct)
    {
        var conversation = ctx.Services.Conversation;
        var asked = ClarificationParser.Format(questions);

        ctx.Feed.Add(
            ActivityKind.Confirmation,
            $"{Title} needs {(questions.Count == 1 ? "an answer" : "some answers")}",
            asked,
            Id);

        // Filed as well as shown. Asking rather than guessing is the whole point of the
        // elicitation, and until now the only record that it had happened was a panel somebody
        // had to be present to see.
        ctx.Record(new RunDecision(
            RunDecisionKind.ClarificationAsked,
            questions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            string.Empty,
            null,
            null,
            asked));

        StatusMessage = questions.Count == 1 ? "Waiting on an answer" : $"Waiting on {questions.Count} answers";

        var outcome = conversation is null
            ? ClarificationOutcome.Unanswered
            : await conversation
                .AskAsync(asked, ctx.RunId, ConversationService.AnswerTimeout, ct)
                .ConfigureAwait(false);

        string followUp;

        if (outcome.Answered)
        {
            followUp = ClarificationParser.DescribeAnswers(questions, outcome.Text);
            ctx.Feed.Info($"{Title}: planning with the answers", outcome.Text);
        }
        else
        {
            var assumed = ClarificationParser.DescribeAssumption(questions);
            followUp = $"{assumed}{Environment.NewLine}{Environment.NewLine}Plan now. Do not ask again.";

            StatusMessage = "Proceeding on an assumption";
            ctx.Feed.Info($"{Title}: nobody answered, so it assumed", assumed);
            conversation?.Report(assumed, ctx.RunId);
        }

        return await planner
            .AnswerAsync(
                PlanPrompt.PlannerSystemPrompt,
                $"{originalMessage}{Environment.NewLine}{Environment.NewLine}{followUp}",
                ctx.ForNode(plannerNode),
                ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["candidateLimit"] = CandidateLimit,
        ["mapCharacters"] = MapCharacters,
        ["candidateCharacters"] = CandidateCharacters,
        ["emittedCharacters"] = EmittedCharacters
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        CandidateLimit = settings["candidateLimit"]?.GetValue<int>() ?? DefaultCandidateLimit;
        MapCharacters = settings["mapCharacters"]?.GetValue<int>() ?? 4000;
        CandidateCharacters = settings["candidateCharacters"]?.GetValue<int>() ?? 16000;
        EmittedCharacters = settings["emittedCharacters"]?.GetValue<int>() ?? 4000;
    }

    /// <summary>
    /// Turns parsed rows into tasks, attaching to each the part of the project it needs and the
    /// current contents of the file when it is an edit.
    /// </summary>
    private FilePlan BuildPlan(
        NodeExecutionContext ctx,
        ProjectIndexService index,
        string projectPath,
        PlanParser.ParsedPlan parsed,
        string map,
        string candidateSummary,
        ContextBudget budget)
    {
        var context = BuildSharedContext(map, candidateSummary, budget);
        var draft = new List<CodeTask>();
        var order = 0;

        foreach (var row in parsed.Rows.Take(MaximumFiles))
        {
            var existing = index.FindFile(row.RelativePath);
            var absolute = Path.Combine(projectPath, row.RelativePath.Replace('/', Path.DirectorySeparatorChar));

            string? contents = null;

            if (row.Operation == FileOperation.Edit)
            {
                contents = ReadIfPresent(absolute);

                if (contents is null)
                {
                    // A plan to edit something that is not there is a plan to create it. Saying so
                    // is better than refusing a row whose intent is perfectly clear.
                    ctx.Feed.Info(
                        $"{Title}: {row.RelativePath} does not exist yet",
                        "It was planned as an edit and will be created instead.");
                }
            }

            var operation = contents is null ? FileOperation.Create : FileOperation.Edit;

            // What this row reuses rather than duplicates. Read from the index, which is the
            // authority on what the project has, and left null when there is genuinely nothing
            // there. Nothing is decided here that was not decided already; it is written down.
            var reused = operation == FileOperation.Edit ? existing : null;
            var reusedType = reused?.Types
                .FirstOrDefault(t => string.Equals(t.Name, row.TypeName, StringComparison.OrdinalIgnoreCase))
                ?? reused?.Types.FirstOrDefault();

            draft.Add(new CodeTask(
                ++order,
                row.RelativePath,
                row.TypeName,
                operation,
                row.Intent,
                context,
                contents,
                reusedType?.FullName,
                reused?.RelativePath));
        }

        var (allowed, blocked) = DuplicateTypeGuard.Filter(index, draft);

        // Renumbering after the guard keeps the order contiguous, which is what the coder is shown.
        var tasks = allowed
            .Select((t, i) => new CodeTask(
                i + 1, t.RelativePath, t.TypeName, t.Operation, t.Intent, t.ProjectContext, t.ExistingContent,
                t.ExistingType, t.ExistingTypePath))
            .ToList();

        var creates = tasks.Count(t => t.Operation == FileOperation.Create);
        var edits = tasks.Count - creates;

        var summary = tasks.Count == 0
            ? "Nothing to write."
            : $"{creates} to create, {edits} to edit";

        return new FilePlan(tasks, parsed.Verdicts, blocked, summary);
    }

    /// <summary>
    /// What every task in this plan is shown about the project. Shared rather than rebuilt per
    /// file because it is the same for all of them and rebuilding it would multiply the cost of a
    /// plan by the number of files in it.
    /// </summary>
    private static string BuildSharedContext(string map, string candidateSummary, ContextBudget budget)
    {
        var builder = new StringBuilder();

        if (map.Length > 0)
        {
            builder.AppendLine("Files already in this project:");
            builder.AppendLine(map);
            builder.AppendLine();
        }

        if (candidateSummary.Length > 0)
        {
            builder.AppendLine("The parts of it this request concerns, in detail:");
            builder.AppendLine(candidateSummary);
        }

        return ContextBudget.Fit(builder.ToString().TrimEnd(), budget.MapCharacters + budget.CandidateCharacters, "project context");
    }

    private void Report(NodeExecutionContext ctx, FilePlan plan)
    {
        LastDecisions = plan.Verdicts.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, plan.Verdicts.Select(v => v.ToString()));

        LastPlan = plan.Tasks.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, plan.Tasks.Select(t => t.ToString()));

        LastBlocked = plan.Blocked.Count == 0
            ? string.Empty
            : string.Join(Environment.NewLine, plan.Blocked);

        // Filed on the run as well as written to the feed. The feed is what somebody reads while
        // this happens; the run is where it can be counted afterwards. Both of these are
        // judgements the application made on the person's behalf, and until now the only record
        // of either was a sentence.
        foreach (var verdict in plan.Verdicts)
        {
            ctx.Record(new RunDecision(
                RunDecisionKind.CandidateVerdict,
                verdict.Decision.ToString(),
                verdict.RelativePath,
                verdict.Symbol,
                null,
                verdict.ToString()));
        }

        foreach (var refusal in plan.Blocked)
        {
            ctx.Record(new RunDecision(
                RunDecisionKind.DuplicateRefused,
                nameof(DuplicateTypeGuard),
                refusal.PlannedPath,
                refusal.TypeName,
                refusal.ExistingPath,
                refusal.Message));
        }

        if (plan.Verdicts.Count > 0)
        {
            ctx.Feed.Info($"{Title}: decisions about what already exists", LastDecisions);
        }

        if (plan.Blocked.Count > 0)
        {
            ctx.Feed.Info($"{Title}: refused as a duplicate", LastBlocked);
        }

        if (plan.Tasks.Count > 0)
        {
            ctx.Feed.Info($"{Title}: {plan.Summary}", LastPlan);
        }
    }

    private static string? ReadIfPresent(string absolutePath)
    {
        try
        {
            return File.Exists(absolutePath) ? File.ReadAllText(absolutePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
