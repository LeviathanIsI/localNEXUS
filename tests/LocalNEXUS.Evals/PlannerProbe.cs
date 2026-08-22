using System.IO;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Editing;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.Processes;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.App.Services.Python;

namespace LocalNEXUS.Evals;

/// <summary>
/// Asks the planner one task's question and prints exactly what came back.
/// </summary>
/// <remarks>
/// A task that fails ten times out of ten fails for a reason, and the reason is in the reply that
/// nothing keeps. Triage parses the reply and throws the text away, so a plan that will not parse
/// reports an empty plan and nothing about what it was given.
///
/// This builds the same message Triage builds, from the same index and the same budget, sends it to
/// the same model, and prints the reply beside what each parser makes of it. It changes nothing and
/// scores nothing.
/// </remarks>
public sealed class PlannerProbe : IDisposable
{
    private readonly DispatcherLoop _loop = new();
    private readonly ChildProcessGroup _children = new();
    private readonly ActivityFeed _feed;
    private readonly RuntimeResolver _runtimes;
    private readonly EvalOptions _options;

    public PlannerProbe(EvalOptions options)
    {
        _options = options;
        _feed = new ActivityFeed(_loop.Dispatcher);

        _runtimes = new RuntimeResolver(
            new LlamaServerManager(_children),
            new PythonRuntimeManager(_children, new PythonProvisioner(_children, _feed, _loop.Dispatcher)));
    }

    /// <summary>Runs the probe over every named task and writes the transcript.</summary>
    public async Task RunAsync(string modelPath, IReadOnlyList<EvalTask> tasks, string outputPath, CancellationToken ct)
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine("# What the planner actually replies");
        text.AppendLine();
        text.AppendLine($"{Path.GetFileName(modelPath)} at temperature {_options.Temperature}, {DateTimeOffset.Now:yyyy-MM-dd HH:mm}.");
        text.AppendLine();

        var endpoint = await _runtimes
            .ServeAsync(modelPath, new ModelRuntimeOptions { ContextSize = _options.ContextSize, GpuLayers = _options.GpuLayers }, null, ct)
            .ConfigureAwait(false);

        var client = new OpenAiCompatibleClient();

        try
        {
            foreach (var task in tasks)
            {
                ct.ThrowIfCancellationRequested();
                Console.WriteLine($"  {task.Id}");

                await ProbeAsync(client, endpoint, task, text, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _runtimes.ShutdownAll();
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, text.ToString());

        Console.WriteLine(text.ToString());
        Console.WriteLine($"Written to {outputPath}");
    }

    private async Task ProbeAsync(
        IModelClient client,
        RuntimeEndpoint endpoint,
        EvalTask task,
        System.Text.StringBuilder text,
        CancellationToken ct)
    {
        using var project = ScratchProject.Create(task);

        var index = new ProjectIndexService();
        await index.EnsureAsync(project.Root, null, ct).ConfigureAwait(false);

        // The same budget a Triage node starts from, so the prompt is the one a run would send.
        var budget = new App.Nodes.TriageNode().Budget;

        var candidates = RelevanceRanker.Rank(index, task.Request, budget.CandidateLimit);
        var map = ProjectDigest.BuildMap(index, candidates, budget);
        var summary = ProjectDigest.BuildCandidateSummary(candidates, budget);
        var message = PlanPrompt.BuildPlannerMessage(task.Request, map, summary, budget, ProjectKind.Unity);

        // What the application decides before the model is asked anything. A request that names
        // nothing never reaches the planner in a real run, so showing the model's answer without
        // this would describe a path that no longer happens.
        var plannable = RequestScope.IsPlannable(task.Request, index);
        var asked = plannable
            ? Array.Empty<App.Services.History.ClarificationQuestion>()
            : RequestScope.AskWhichOne(task.Request, index, candidates).ToArray();

        var result = await client
            .StreamChatAsync(
                new ModelEndpoint(endpoint.BaseUrl, endpoint.ModelId, null),
                PlanPrompt.PlannerSystemPromptFor(ProjectKind.Unity),
                message,
                _options.Temperature,
                _options.MaxTokens,
                null,
                ct)
            .ConfigureAwait(false);

        var reply = result.Text ?? string.Empty;
        var questions = ClarificationParser.Parse(reply);
        var parsed = PlanParser.Parse(reply);

        text.AppendLine("---");
        text.AppendLine();
        text.AppendLine($"## {task.Id}");
        text.AppendLine();
        text.AppendLine($"**Request:** {task.Request}");
        text.AppendLine();
        text.AppendLine($"**Does the request name anything?** {(plannable ? "Yes, so it is planned." : "No, so the run asks instead of planning.")}");

        foreach (var question in asked)
        {
            text.AppendLine();
            text.AppendLine($"> {question.Text}");
            text.AppendLine($"> Options: {string.Join(", ", question.Options)}");
        }

        text.AppendLine();
        text.AppendLine("Everything below is what the planner would have said had it been asked, which is what this "
            + "probe sends it regardless, so the two can be compared.");
        text.AppendLine();
        text.AppendLine($"**Finish reason:** {result.FinishReason}. **Reply length:** {reply.Length} characters.");
        text.AppendLine();
        text.AppendLine($"**ClarificationParser:** {questions.Count} question(s).");

        foreach (var question in questions)
        {
            text.AppendLine($"  - {question.Text} ({question.Options.Count} option(s))");
        }

        text.AppendLine();
        text.AppendLine($"**PlanParser:** {parsed.Rows.Count} plan row(s), {parsed.Verdicts.Count} decision row(s).");

        foreach (var row in parsed.Rows)
        {
            text.AppendLine($"  - {row.Operation} {row.RelativePath} ({row.TypeName})");
        }

        text.AppendLine();
        text.AppendLine("**The reply, verbatim:**");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine(reply.Trim());
        text.AppendLine("```");
        text.AppendLine();

        await ProbeCoderAsync(client, endpoint, task, project, parsed, text, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the first edit row of the plan to the coder and shows whether anything changed.
    /// </summary>
    /// <remarks>
    /// A task whose plan is right and whose file comes back byte identical has failed somewhere
    /// after planning, and this is where to look. It answers whether the model declined to make
    /// the change or whether the reply was a diff that applied to nothing.
    /// </remarks>
    private async Task ProbeCoderAsync(
        IModelClient client,
        RuntimeEndpoint endpoint,
        EvalTask task,
        ScratchProject project,
        PlanParser.ParsedPlan parsed,
        System.Text.StringBuilder text,
        CancellationToken ct)
    {
        var row = parsed.Rows.FirstOrDefault(r => r.Operation == FileOperation.Edit);

        if (row.RelativePath is null)
        {
            return;
        }

        var absolute = Path.Combine(project.Root, row.RelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(absolute))
        {
            return;
        }

        var existing = await File.ReadAllTextAsync(absolute, ct).ConfigureAwait(false);

        var codeTask = new CodeTask(
            1, row.RelativePath, row.TypeName, FileOperation.Edit, row.Intent, string.Empty, existing);

        var wholeFile = CodeEditApplier.WantsWholeFile(EditFormat.Automatic, false, existing.Length);
        var message = PlanPrompt.BuildCoderMessage(codeTask, string.Empty, wholeFile);

        var result = await client
            .StreamChatAsync(
                new ModelEndpoint(endpoint.BaseUrl, endpoint.ModelId, null),
                App.Nodes.ModelNode.DefaultSystemPrompt,
                message,
                _options.Temperature,
                _options.MaxTokens,
                null,
                ct)
            .ConfigureAwait(false);

        var coderReply = result.Text ?? string.Empty;

        text.AppendLine($"**Coder, asked for {(wholeFile ? "the whole file" : "a diff")} of {row.RelativePath}.**");
        text.AppendLine();

        string applied;
        try
        {
            applied = CodeEditApplier.Apply(coderReply, existing);
        }
        catch (Exception ex)
        {
            applied = string.Empty;
            text.AppendLine($"Applying it threw: {ex.GetType().Name}: {ex.Message}");
            text.AppendLine();
        }

        var changed = applied.Length > 0
                      && !string.Equals(
                          applied.ReplaceLineEndings("\n").TrimEnd(),
                          existing.ReplaceLineEndings("\n").TrimEnd(),
                          StringComparison.Ordinal);

        text.AppendLine($"After applying: {(changed ? "the file changed" : "**the file is unchanged**")}.");
        text.AppendLine();
        text.AppendLine("**What the coder replied, verbatim:**");
        text.AppendLine();
        text.AppendLine("```");
        text.AppendLine(coderReply.Trim());
        text.AppendLine("```");
        text.AppendLine();
    }

    public void Dispose()
    {
        _runtimes.ShutdownAll();
        _children.Dispose();
        _loop.Dispose();
    }
}
