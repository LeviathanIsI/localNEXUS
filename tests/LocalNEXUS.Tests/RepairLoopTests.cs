using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Code that does not compile going back for another attempt.
/// </summary>
/// <remarks>
/// The loop belongs to the node, not the executor: the check follows its own incoming wire, asks
/// whatever it finds there whether it can revise, and hands over the failing code and the errors.
/// No node type is named anywhere in it, which is why every test here wires up a node the
/// application has never heard of.
///
/// A cycle in the graph is deliberately not the mechanism, and the executor rejecting cycles is
/// tested separately. A retry count belongs in a setting, not in how many times somebody drew a
/// loop.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class RepairLoopTests
{
    private const string Broken = "public class Thing { public int Go() { return 1 + } }";
    private const string Fixed = "public class Thing { public int Go() { return 1; } }";
    private const string AlsoBroken = "public class Thing { public int Go() { return ; } }";

    private static (GraphModel Graph, CompilerCheckNode Check, RepairableNode Coder) Wire(
        string first,
        int retryLimit = 2)
    {
        var graph = new GraphModel();

        var coder = new RepairableNode("coder", first);
        var check = new CompilerCheckNode
        {
            RetryLimit = retryLimit,
            FailureBehaviour = CompileFailureBehaviour.FaultTheRun
        };

        graph.AddNode(coder);
        graph.AddNode(check);
        Assert.True(graph.TryConnect(coder.Out, check.Code, out _));

        return (graph, check, coder);
    }

    private static Task<RunContext> Run(TestServices services, GraphModel graph)
        => new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

    /// <summary>Code that compiles passes straight through without anybody being asked.</summary>
    [Fact]
    public async Task WorkingCodeIsNotRepaired()
    {
        using var services = TestServices.Create();
        var (graph, check, coder) = Wire(Fixed);

        var run = await Run(services, graph);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(CompileOutcome.Compiled, check.Outcome);
        Assert.Empty(coder.Requests);
    }

    /// <summary>Broken code goes back once and the repaired version is what comes out.</summary>
    [Fact]
    public async Task BrokenCodeIsSentBackAndTheRepairIsEmitted()
    {
        using var services = TestServices.Create();
        var (graph, check, coder) = Wire(Broken);
        coder.Attempts(Fixed);

        var run = await Run(services, graph);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(CompileOutcome.Repaired, check.Outcome);
        Assert.Single(coder.Requests);

        Assert.True(run.TryGetValue(check.Checked, out var emitted));
        Assert.Equal(Fixed, emitted);
    }

    /// <summary>
    /// The errors are actually sent, which is the part that is easy to get wrong invisibly.
    /// </summary>
    /// <remarks>
    /// A loop that runs the right number of times and hands over no diagnostics looks exactly like
    /// a working repair loop from the outside, and produces a model guessing at what was wrong.
    /// </remarks>
    [Fact]
    public async Task TheDiagnosticsAreSentWithTheCode()
    {
        using var services = TestServices.Create();
        var (graph, check, coder) = Wire(Broken);
        coder.Attempts(Fixed);

        await Run(services, graph);

        var request = Assert.Single(coder.Requests);

        Assert.Equal(Broken, request.FailingCode);
        Assert.NotEmpty(request.Diagnostics);
        Assert.True(request.ErrorCount > 0);
        Assert.False(string.IsNullOrWhiteSpace(request.FormattedDiagnostics));
    }

    /// <summary>Each attempt is numbered against the limit, so the model knows where it is.</summary>
    [Fact]
    public async Task EachAttemptIsNumbered()
    {
        using var services = TestServices.Create();
        var (graph, check, coder) = Wire(Broken, retryLimit: 3);
        coder.Attempts(AlsoBroken, AlsoBroken, Fixed);

        await Run(services, graph);

        Assert.Equal(3, coder.Requests.Count);
        Assert.Equal(new[] { 1, 2, 3 }, coder.Requests.Select(r => r.Attempt));
        Assert.All(coder.Requests, r => Assert.Equal(3, r.AttemptLimit));
    }

    /// <summary>The retry cap is a cap, and it is the setting rather than anything drawn.</summary>
    [Fact]
    public async Task TheRetryLimitIsRespected()
    {
        using var services = TestServices.Create();
        var (graph, check, coder) = Wire(Broken, retryLimit: 2);
        coder.Attempts(AlsoBroken, AlsoBroken, Fixed);

        var run = await Run(services, graph);

        Assert.Equal(2, coder.Requests.Count);
        Assert.Equal(CompileOutcome.Failed, check.Outcome);
        Assert.Equal(RunState.Faulted, run.State);
    }

    /// <summary>A limit of zero means no repair at all, not an unbounded one.</summary>
    [Fact]
    public async Task ALimitOfZeroDisablesRepair()
    {
        using var services = TestServices.Create();
        var (graph, check, coder) = Wire(Broken, retryLimit: 0);
        coder.Attempts(Fixed);

        await Run(services, graph);

        Assert.Empty(coder.Requests);
        Assert.Equal(CompileOutcome.Failed, check.Outcome);
    }

    /// <summary>Something upstream that cannot revise is reported rather than crashed into.</summary>
    [Fact]
    public async Task SomethingThatCannotReviseIsReported()
    {
        using var services = TestServices.Create();

        var graph = new GraphModel();
        var plain = new RecordingNode("plain", new List<string>(), PinType.Code) { Append = Broken };
        var check = new CompilerCheckNode
        {
            RetryLimit = 3,
            FailureBehaviour = CompileFailureBehaviour.FaultTheRun
        };

        graph.AddNode(plain);
        graph.AddNode(check);
        Assert.True(graph.TryConnect(plain.Out, check.Code, out _));

        var run = await Run(services, graph);

        Assert.Equal(RunState.Faulted, run.State);
        Assert.Equal(CompileOutcome.Failed, check.Outcome);
    }

    /// <summary>A source that declines to repair is taken at its word and not asked.</summary>
    [Fact]
    public async Task ASourceThatDeclinesIsNotAsked()
    {
        using var services = TestServices.Create();
        var (graph, check, coder) = Wire(Broken, retryLimit: 3);

        coder.Refuse = "no model is wired into me";
        coder.Attempts(Fixed);

        await Run(services, graph);

        Assert.Empty(coder.Requests);
        Assert.Equal(CompileOutcome.Failed, check.Outcome);
    }

    /// <summary>
    /// A check with nothing wired to it refuses rather than passing an empty file through.
    /// </summary>
    [Fact]
    public async Task AnUnwiredCheckRefuses()
    {
        using var services = TestServices.Create();

        var graph = new GraphModel();
        var check = new CompilerCheckNode();
        graph.AddNode(check);

        Assert.Equal(RunState.Faulted, (await Run(services, graph)).State);
    }

    /// <summary>
    /// A failure can stage the file instead of stopping the run, and the run says which happened.
    /// </summary>
    /// <remarks>
    /// The all or nothing behaviour was the earlier design and it threw away four working files
    /// because a fifth did not build. A run that resolved some of what it was asked and not the
    /// rest is its own state rather than a success or a failure.
    /// </remarks>
    [Fact]
    public async Task AFailureCanStageInsteadOfStopping()
    {
        using var services = TestServices.Create();
        var (graph, check, coder) = Wire(Broken, retryLimit: 0);

        check.FailureBehaviour = CompileFailureBehaviour.StageForLater;

        var run = await Run(services, graph);

        Assert.NotEqual(RunState.Faulted, run.State);
        Assert.Equal(CompileOutcome.Failed, check.Outcome);
    }

    /// <summary>A failure can also be a warning that lets the run carry on.</summary>
    [Fact]
    public async Task AFailureCanBeAWarningInstead()
    {
        using var services = TestServices.Create();
        var (graph, check, coder) = Wire(Broken, retryLimit: 0);

        check.FailureBehaviour = CompileFailureBehaviour.ContinueWithWarning;

        var run = await Run(services, graph);

        Assert.NotEqual(RunState.Faulted, run.State);
    }

    /// <summary>
    /// The node says what it can reach before a run, not after one.
    /// </summary>
    /// <remarks>
    /// A real probe rather than a guess from whether a folder exists, so a Unity install that is
    /// present but unreadable answers unreadable.
    /// </remarks>
    [Fact]
    public void ReachabilityIsReportedBeforeARun()
    {
        using var services = TestServices.Create();
        var check = new CompilerCheckNode();

        check.RefreshReachability(services.Services.Compiler, projectPath: null);

        Assert.False(string.IsNullOrWhiteSpace(check.ReachabilityText));
        Assert.False(string.IsNullOrWhiteSpace(check.ReachabilityDetail));
        Assert.True(check.ReachabilityIsPartial);
    }
}
