using System.IO;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Debate;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.App.Services.Python;

namespace LocalNEXUS.Evals;

/// <summary>
/// Runs one debate and one judgement, and prints everything that happened.
/// </summary>
/// <remarks>
/// Deliberately not a scored task. Debate produces a brief rather than a file, so none of the
/// twenty tasks can measure it: there is nothing on disk afterwards to count. What is worth having
/// is the transcript, the convergence number after each round, and the two models' own accounts of
/// how close they had come, so that somebody can read all three and judge whether the measurement
/// agrees with what the argument actually did.
///
/// Both debaters are the same weights, because one model is what is on this machine. Two separate
/// nodes means two separate conversations, which is a real debate in the sense that each only sees
/// the other's last position, but it is not two different models and nothing here should be read
/// as if it were.
/// </remarks>
public sealed class DebateProbe : IDisposable
{
    private readonly DispatcherLoop _loop = new();
    private readonly ChildProcessGroup _children = new();
    private readonly ActivityFeed _feed;
    private readonly AppConfig _config = new();
    private readonly RuntimeResolver _runtimes;
    private readonly MeshManager _mesh;
    private readonly ExtensionRegistry _extensions;
    private readonly NodeFactory _factory;
    private readonly RoslynUnityCompiler _compiler;
    private readonly EvalOptions _options;

    public DebateProbe(EvalOptions options)
    {
        _options = options;

        var dispatcher = _loop.Dispatcher;
        _feed = new ActivityFeed(dispatcher);

        _runtimes = new RuntimeResolver(
            new LlamaServerManager(_children),
            new PythonRuntimeManager(_children, new PythonProvisioner(_children, _feed, dispatcher)));

        _mesh = new MeshManager(_config, _feed, dispatcher, _children);
        _extensions = new ExtensionRegistry(_feed);
        _compiler = new RoslynUnityCompiler(new UnityReferenceResolver());

        _factory = new NodeFactory(
            new ModelCatalog(_config),
            _mesh,
            new SilentDialogService(),
            _config,
            _extensions,
            new ExtensionHost(_children, _feed),
            new InMemoryCredentialStore());
    }

    /// <summary>The thing the two are asked to argue about.</summary>
    /// <remarks>
    /// Chosen because it has two defensible answers that a competent engineer would actually
    /// disagree about, rather than a right one and a wrong one. A subject with an obvious answer
    /// measures nothing: both sides say the same thing in round one and it settles immediately.
    /// </remarks>
    public const string Subject =
        "We need to store inventory items in a Unity game. One option is a ScriptableObject per item type, "
        + "authored in the editor and referenced by the runtime. The other is plain C# classes built from a "
        + "JSON file loaded at startup. Decide which this project should use and say what decided it.";

    /// <summary>Runs it and writes the whole transcript to the given file, and to the console.</summary>
    public async Task RunAsync(string modelPath, string outputPath, CancellationToken ct)
    {
        var graph = new GraphModel { Name = "debate probe" };

        var prompt = (PromptNode)_factory.Create("Prompt");
        var first = (ModelNode)_factory.Create("Model");
        var second = (ModelNode)_factory.Create("Model");
        var debate = (DebateNode)_factory.Create("Debate");
        var judge = (JudgeNode)_factory.Create("Judge");

        first.Title = "model a";
        second.Title = "model b";

        foreach (var model in new[] { first, second })
        {
            model.Provider = ModelProvider.Local;
            model.ModelFilePath = modelPath;
            model.ContextSize = _options.ContextSize;
            model.GpuLayers = _options.GpuLayers;
            model.MaxTokens = _options.MaxTokens;

            // Higher than the coder runs at. Two debaters sampled at nearly zero produce the same
            // answer twice, which is not a debate and would make the measurement meaningless.
            model.Temperature = 0.7d;
        }

        // Opposed on purpose, because two models both told to debate agree in round one more often
        // than not, and the convergence function has nothing to measure if they never differ.
        debate.FirstRole = DebateRole.Defend;
        debate.SecondRole = DebateRole.Criticize;
        debate.ConvergenceThreshold = 75;
        debate.Arbiter = DebateArbiter.Second;

        judge.Mode = JudgeMode.Combine;

        foreach (var node in new NodeBase[] { prompt, first, second, debate, judge })
        {
            graph.AddNode(node);
        }

        Connect(graph, prompt.Request, debate.Subject);
        Connect(graph, first.Self, debate.FirstModel);
        Connect(graph, second.Self, debate.SecondModel);
        Connect(graph, debate.Brief, judge.First);
        Connect(graph, first.Self, judge.Judge);

        var services = BuildServices();

        Console.WriteLine("Running the debate. This takes a few minutes.");

        RunContext? run = null;
        string? fault = null;

        try
        {
            run = await new GraphExecutor(services).RunAsync(graph, Subject, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            fault = $"{ex.GetType().Name}: {ex.Message}";
        }

        _runtimes.ShutdownAll();

        var transcript = Format(run, fault, debate, judge);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        File.WriteAllText(outputPath, transcript);

        Console.WriteLine(transcript);
        Console.WriteLine($"Written to {outputPath}");
    }

    /// <summary>
    /// Everything the run said, in order.
    /// </summary>
    /// <remarks>
    /// Read out of the activity feed, which is where a debate reports itself and the only place the
    /// per round numbers exist. That is prose coupling and it would be the wrong way to build a
    /// metric, which is why nothing here is one: this is a transcript for a person to read.
    /// </remarks>
    private string Format(RunContext? run, string? fault, DebateNode debate, JudgeNode judge)
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine("# Debate and Judge, one run");
        text.AppendLine();
        text.AppendLine($"Ran at {DateTimeOffset.Now:yyyy-MM-dd HH:mm}. Outcome: {run?.State.ToString() ?? "never started"}.");

        if (fault is not null)
        {
            text.AppendLine();
            text.AppendLine($"**It stopped:** {fault}");
        }

        text.AppendLine();
        text.AppendLine("## Settings");
        text.AppendLine();
        text.AppendLine($"- Roles: first {debate.FirstRole}, second {debate.SecondRole}");
        text.AppendLine($"- Settles at {debate.ConvergenceThreshold} percent, at most {DebateNode.MaximumRounds} rounds");
        text.AppendLine($"- Arbiter: {debate.Arbiter}");
        text.AppendLine($"- Judge mode: {judge.Mode}");
        text.AppendLine($"- Weighting: {ConvergenceMeter.WeightingSummary}");
        text.AppendLine();
        text.AppendLine("## Subject");
        text.AppendLine();
        text.AppendLine(Subject);
        text.AppendLine();
        text.AppendLine("## Transcript");
        text.AppendLine();

        foreach (var entry in _feed.Events)
        {
            text.AppendLine($"### {entry.Title}");
            text.AppendLine();

            if (!string.IsNullOrWhiteSpace(entry.Text))
            {
                text.AppendLine(entry.Text.Trim());
                text.AppendLine();
            }
        }

        text.AppendLine("## What the nodes ended on");
        text.AppendLine();
        text.AppendLine($"- Debate outcome: {debate.LastOutcome}");
        text.AppendLine();
        text.AppendLine("### The verdict");
        text.AppendLine();
        text.AppendLine(string.IsNullOrWhiteSpace(judge.LastVerdict) ? "(nothing)" : judge.LastVerdict.Trim());

        return text.ToString();
    }

    private ExecutionServices BuildServices()
    {
        // No project, because a debate produces a brief rather than a file and nothing here writes
        // anything. Grounding is left on own reasoning for the same reason.
        var unityProject = new UnityProjectService();

        return new ExecutionServices(
            new ModelClientRouter(new OpenAiCompatibleClient(), new AnthropicClient(), new GeminiClient()),
            _runtimes,
            _mesh,
            _compiler,
            new ProjectIndexService(),
            unityProject,
            new FileWriter(),
            _feed,
            new StagingStore(_loop.Dispatcher),
            null,
            null,
            _extensions,
            null,
            new InMemoryCredentialStore(),
            new RunCostTracker());
    }

    private static void Connect(GraphModel graph, Pin source, Pin target)
    {
        if (!graph.TryConnect(source, target, out var why))
        {
            throw new InvalidOperationException($"The debate graph could not be wired: {why}");
        }
    }

    public void Dispose()
    {
        _runtimes.ShutdownAll();
        _children.Dispose();
        _loop.Dispose();
    }
}
