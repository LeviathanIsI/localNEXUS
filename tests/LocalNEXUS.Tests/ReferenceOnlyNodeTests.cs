using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Debate;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// A node the graph reads rather than runs.
/// </summary>
/// <remarks>
/// A model wire carries a reference to a configured model: the consumer needs the model to exist,
/// not to have run. The sort stopped treating those as dependencies in v1.16 and the node was
/// executed anyway, so a model handed to a debate threw for having nothing on its own prompt pin
/// and the run stopped before a word was exchanged. Debate and Judge had never run.
///
/// The rule is about outgoing use rather than about the node, and every test here is written
/// against a node type the application has never heard of, which is also what holds the executor
/// to knowing nothing about node types.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ReferenceOnlyNodeTests
{
    /// <summary>A judge wired to a model and given something to judge, which is the ordinary shape.</summary>
    private static (GraphModel Graph, ScriptedModelNode Model, JudgeNode Judge) Wire()
    {
        var graph = new GraphModel();

        var model = new ScriptedModelNode("judge model").ThenAlways("the verdict");
        var judge = new JudgeNode();
        var position = new RecordingNode("position", new List<string>()) { Append = "a position" };

        graph.AddNode(model);
        graph.AddNode(judge);
        graph.AddNode(position);

        Assert.True(graph.TryConnect(model.Self, judge.Judge, out _));
        Assert.True(graph.TryConnect(position.Out, judge.First, out _));

        return (graph, model, judge);
    }

    /// <summary>
    /// Wired only as a reference, it does not execute, and the run does not fault.
    /// </summary>
    /// <remarks>
    /// The defect exactly. Before this, the run stopped here with the model complaining that
    /// nothing was on its text pin, which was true and was the point.
    /// </remarks>
    [Fact]
    public async Task AReferenceOnlyNodeDoesNotExecute()
    {
        using var services = TestServices.Create();
        var (graph, model, judge) = Wire();

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.False(model.Executed);

        // The consumer still got what it needed, which is the whole reason for not running it.
        Assert.Equal(1, model.AnswerCount);
        Assert.Equal("the verdict", judge.LastVerdict);
    }

    /// <summary>Not running it is not the same as hiding it. The node says why it did nothing.</summary>
    [Fact]
    public async Task AReferenceOnlyNodeSaysWhyItDidNothing()
    {
        using var services = TestServices.Create();
        var (graph, model, _) = Wire();

        await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(model.StatusMessage));
    }

    /// <summary>
    /// The same node with a second outgoing wire is a step as well, and runs.
    /// </summary>
    /// <remarks>
    /// The qualifier that makes this a rule about outgoing use rather than about the node. A model
    /// can be handed to a debate and also be the thing writing a file, and something downstream is
    /// then waiting on what it says.
    /// </remarks>
    [Fact]
    public async Task ANodeAlsoWiredAsAStepStillExecutes()
    {
        using var services = TestServices.Create();
        var (graph, model, _) = Wire();

        var reader = new RecordingNode("reader", new List<string>());
        graph.AddNode(reader);
        Assert.True(graph.TryConnect(model.Reply, reader.In, out _));

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.True(model.Executed);
        Assert.Equal("ran", reader.Received);
    }

    /// <summary>A node wired to nothing at all is left alone and still runs.</summary>
    /// <remarks>
    /// Whether an unconnected node should execute is a different question with a different answer.
    /// This says the fix did not quietly answer it too.
    /// </remarks>
    [Fact]
    public async Task ANodeWiredToNothingStillExecutes()
    {
        using var services = TestServices.Create();

        var graph = new GraphModel();
        var alone = new RecordingNode("alone", new List<string>());
        graph.AddNode(alone);

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(1, alone.Runs);
    }

    /// <summary>The question is asked of pin types, so it answers the same for any node type.</summary>
    [Fact]
    public void TheRuleIsAboutPinsRatherThanNodes()
    {
        var graph = new GraphModel();

        var model = new ScriptedModelNode("model");
        var judge = new JudgeNode();
        var reader = new RecordingNode("reader", new List<string>());

        graph.AddNode(model);
        graph.AddNode(judge);
        graph.AddNode(reader);

        Assert.False(GraphTopology.IsReferenceOnly(graph, model));

        graph.TryConnect(model.Self, judge.Judge, out _);
        Assert.True(GraphTopology.IsReferenceOnly(graph, model));

        graph.TryConnect(model.Reply, reader.In, out _);
        Assert.False(GraphTopology.IsReferenceOnly(graph, model));

        // And a node with no model pin at all is never a reference, whatever it is wired to.
        Assert.False(GraphTopology.IsReferenceOnly(graph, reader));
    }

    /// <summary>
    /// A debate wired the way the Model pin was introduced for runs end to end.
    /// </summary>
    /// <remarks>
    /// Two models handed to a debate and nothing on either of their own prompt pins, which is the
    /// graph that faulted immediately and forced the overnight probe to feed both models a subject
    /// they did not need.
    /// </remarks>
    [Fact]
    public async Task ADebateRunsFromAnUnmodifiedGraph()
    {
        using var services = TestServices.Create();

        var graph = new GraphModel();

        var subject = new RecordingNode("subject", new List<string>()) { Append = "how should the inventory work" };
        var first = new ScriptedModelNode("model a").ThenAlways("Put stacking on InventorySlot.");
        var second = new ScriptedModelNode("model b").ThenAlways("Put stacking on InventorySlot.");
        var debate = new DebateNode { ConvergenceThreshold = 70 };
        var judge = new JudgeNode { Mode = JudgeMode.Combine };

        foreach (var node in new NodeBase[] { subject, first, second, debate, judge })
        {
            graph.AddNode(node);
        }

        Assert.True(graph.TryConnect(subject.Out, debate.Subject, out _));
        Assert.True(graph.TryConnect(first.Self, debate.FirstModel, out _));
        Assert.True(graph.TryConnect(second.Self, debate.SecondModel, out _));
        Assert.True(graph.TryConnect(debate.Brief, judge.First, out _));
        Assert.True(graph.TryConnect(first.Self, judge.Judge, out _));

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.False(first.Executed);
        Assert.False(second.Executed);
        Assert.False(string.IsNullOrWhiteSpace(judge.LastVerdict));
    }
}
