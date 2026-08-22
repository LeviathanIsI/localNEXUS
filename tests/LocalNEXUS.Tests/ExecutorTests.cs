using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The graph walker: what order nodes run in, what they are handed, and what a failure does.
/// </summary>
/// <remarks>
/// Every test here uses <see cref="RecordingNode"/>, a node type that lives in the test assembly
/// and is not in <c>NodeFactory</c>. That is deliberate: the settled rule is that adding a node
/// type never touches the executor, and the way to keep that true is to prove the executor runs a
/// type it has never seen.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ExecutorTests
{
    [Fact]
    public async Task ANodeRunsAfterEverythingItDependsOn()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var first = new RecordingNode("first", log);
        var second = new RecordingNode("second", log);
        var third = new RecordingNode("third", log);

        graph.AddNode(third);
        graph.AddNode(first);
        graph.AddNode(second);

        // Added out of order on purpose. Order on the canvas is not order of execution.
        Assert.True(graph.TryConnect(first.Out, second.In, out _));
        Assert.True(graph.TryConnect(second.Out, third.In, out _));

        using var services = TestServices.Create();
        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(new[] { "first", "second", "third" }, log);
    }

    /// <summary>A value put on an output pin is what the node on the other end of the wire reads.</summary>
    [Fact]
    public async Task OutputsAreGatheredFromIncomingWires()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var first = new RecordingNode("first", log) { Append = "alpha" };
        var second = new RecordingNode("second", log) { Append = "-beta" };

        graph.AddNode(first);
        graph.AddNode(second);
        graph.TryConnect(first.Out, second.In, out _);

        using var services = TestServices.Create();
        await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal("alpha", second.Received);
    }

    /// <summary>A node with nothing wired to its input reads empty rather than throwing.</summary>
    [Fact]
    public async Task AnUnconnectedInputIsEmpty()
    {
        var log = new List<string>();
        var graph = new GraphModel();
        var only = new RecordingNode("only", log);
        graph.AddNode(only);

        using var services = TestServices.Create();
        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(string.Empty, only.Received);
    }

    /// <summary>
    /// A cycle is refused before anything runs.
    /// </summary>
    /// <remarks>
    /// This matters more than it looks. The repair loop was deliberately not built as a cycle in
    /// the graph, and this is what keeps that decision enforced rather than merely intended.
    /// </remarks>
    [Fact]
    public async Task ACycleIsRefusedAndNothingRuns()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var first = new RecordingNode("first", log);
        var second = new RecordingNode("second", log);

        graph.AddNode(first);
        graph.AddNode(second);
        graph.TryConnect(first.Out, second.In, out _);
        graph.TryConnect(second.Out, first.In, out _);

        using var services = TestServices.Create();
        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Faulted, run.State);
        Assert.Empty(log);
    }

    /// <summary>A Model wire is a reference, not a dependency, so it cannot make a cycle.</summary>
    /// <remarks>
    /// The v1.16 bug exactly: Triage plans into a Model and the same Model is wired back to Triage
    /// to say which model plans. Read as a dependency that is a cycle and the graph refuses to run,
    /// which is what happened.
    /// </remarks>
    [Fact]
    public void AModelWireIsNotADependency()
    {
        using var services = TestServices.Create();

        var graph = new GraphModel();
        var triage = new App.Nodes.TriageNode();
        var model = (App.Nodes.ModelNode)services.Factory.Create("Model")!;

        graph.AddNode(triage);
        graph.AddNode(model);

        Assert.True(graph.TryConnect(triage.Plan, model.Prompt, out _));
        Assert.True(graph.TryConnect(model.Self, triage.Model, out _));

        Assert.True(GraphTopology.Sort(graph).IsAcyclic);
    }

    /// <summary>A failure stops the run and leaves everything after it unrun.</summary>
    [Fact]
    public async Task AFailureStopsTheRun()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var first = new RecordingNode("first", log);
        var second = new RecordingNode("second", log) { FailWith = "deliberate" };
        var third = new RecordingNode("third", log);

        graph.AddNode(first);
        graph.AddNode(second);
        graph.AddNode(third);
        graph.TryConnect(first.Out, second.In, out _);
        graph.TryConnect(second.Out, third.In, out _);

        using var services = TestServices.Create();
        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Faulted, run.State);
        Assert.Equal(NodeState.Faulted, second.State);
        Assert.Equal(NodeState.Pending, third.State);
        Assert.Equal(0, third.Runs);
    }

    /// <summary>Two independent branches both run, and each sees only its own wire.</summary>
    [Fact]
    public async Task IndependentBranchesBothRun()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var source = new RecordingNode("source", log) { Append = "x" };
        var left = new RecordingNode("left", log);
        var right = new RecordingNode("right", log);

        graph.AddNode(source);
        graph.AddNode(left);
        graph.AddNode(right);
        graph.TryConnect(source.Out, left.In, out _);
        graph.TryConnect(source.Out, right.In, out _);

        using var services = TestServices.Create();
        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal("x", left.Received);
        Assert.Equal("x", right.Received);
        Assert.Equal("source", log[0]);
    }

    /// <summary>Cancelling part way leaves the run in a state that is not Completed.</summary>
    [Fact]
    public async Task CancellationDoesNotReportSuccess()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var only = new RecordingNode("only", log);
        graph.AddNode(only);

        using var services = TestServices.Create();
        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", cancellation.Token);

        Assert.NotEqual(RunState.Completed, run.State);
    }
}
