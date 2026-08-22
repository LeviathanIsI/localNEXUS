using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Debate;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Two models in genuine disagreement, and what ends it.
/// </summary>
/// <remarks>
/// The models are scripted, which is what makes any of this testable: a debate is a loop with a
/// stopping condition, and a stopping condition cannot be tested against something that answers
/// differently every time. What is being checked is the loop, the refusals and the limits, never
/// the quality of an argument.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class DebateTests
{
    private static (GraphModel Graph, DebateNode Node, ScriptedModelNode First, ScriptedModelNode Second) Wire(
        int threshold = 70)
    {
        var graph = new GraphModel();

        var node = new DebateNode { ConvergenceThreshold = threshold };
        var first = new ScriptedModelNode("model a");
        var second = new ScriptedModelNode("model b");
        var subject = new RecordingNode("subject", new List<string>()) { Append = "how should the inventory work" };

        graph.AddNode(node);
        graph.AddNode(first);
        graph.AddNode(second);
        graph.AddNode(subject);

        Assert.True(graph.TryConnect(subject.Out, node.Subject, out _));
        Assert.True(graph.TryConnect(first.Self, node.FirstModel, out _));
        Assert.True(graph.TryConnect(second.Self, node.SecondModel, out _));

        return (graph, node, first, second);
    }

    private static async Task<RunContext> Run(TestServices services, GraphModel graph)
        => await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

    /// <summary>Two models that already agree settle in one round.</summary>
    [Fact]
    public async Task AgreementInTheFirstRoundEndsIt()
    {
        using var services = TestServices.Create();
        var (graph, node, first, second) = Wire();

        // Three named things, which is the floor beneath which the meter reports nothing rather
        // than a number. Two identical positions below it never settle, which is correct and is
        // not what this test is about.
        const string position = "InventorySlot should hold an ItemId. "
            + "Stacking belongs on InventorySlot. ItemStack stays out of it.";

        first.ThenAlways(position);
        second.ThenAlways(position);

        var run = await Run(services, graph);

        Assert.Equal(RunState.Completed, run.State);

        // One opening each, and then the arbiter writing the summary. No second round.
        Assert.Equal(1, first.AnswerCount);
        Assert.Equal(2, second.AnswerCount);
    }

    /// <summary>Two models that never agree run out of rounds rather than forever.</summary>
    [Fact]
    public async Task DisagreementStopsAtTheRoundCap()
    {
        using var services = TestServices.Create();
        var (graph, node, first, second) = Wire(threshold: 99);

        first.ThenAlways("InventorySlot should be a struct. Never use a class here. Stacking is wrong.");
        second.ThenAlways("Weapon should be a ScriptableObject. Never use a struct. Pooling matters most.");

        var run = await Run(services, graph);

        Assert.Equal(RunState.Completed, run.State);
        Assert.True(first.AnswerCount <= DebateNode.MaximumRounds + 1, $"first was asked {first.AnswerCount} times");
        Assert.True(second.AnswerCount <= DebateNode.MaximumRounds + 2, $"second was asked {second.AnswerCount} times");
    }

    /// <summary>
    /// A threshold of zero emits after one round, which is two answers rather than a debate.
    /// </summary>
    /// <remarks>
    /// Both ends of the slider are legitimate and neither is obvious, so the node says what the
    /// number means. This checks the end that looks like it should mean "off".
    /// </remarks>
    [Fact]
    public async Task AThresholdOfZeroEmitsAfterOneRound()
    {
        using var services = TestServices.Create();
        var (graph, node, first, second) = Wire(threshold: 0);

        first.ThenAlways("InventorySlot should be a struct and ItemStack belongs on it.");
        second.ThenAlways("WeaponSlot should be a ScriptableObject and ProjectileSpawner matters most.");

        await Run(services, graph);

        Assert.Equal(1, first.AnswerCount);
    }

    /// <summary>
    /// Two positions with nothing measurable in them run the rounds rather than settling.
    /// </summary>
    /// <remarks>
    /// The stopping condition is a measured score, and no score is not a score of zero. A threshold
    /// of zero therefore does not end an unmeasurable debate after one round the way it ends a
    /// measurable one, which is the honest behaviour: nothing was compared, so nothing agreed.
    /// </remarks>
    [Fact]
    public async Task AnUnmeasurablePairDoesNotSettle()
    {
        using var services = TestServices.Create();
        var (graph, node, first, second) = Wire(threshold: 0);

        first.ThenAlways("one");
        second.ThenAlways("two");

        await Run(services, graph);

        Assert.Null(ConvergenceMeter.Measure("one", "two").Score);
        Assert.Equal(DebateNode.MaximumRounds, first.AnswerCount);
    }

    /// <summary>Two models told to defend are not a debate, and the node refuses to pretend.</summary>
    [Fact]
    public async Task TwoDefendersAreRefused()
    {
        using var services = TestServices.Create();
        var (graph, node, first, second) = Wire();

        node.FirstRole = DebateRole.Defend;
        node.SecondRole = DebateRole.Defend;
        first.ThenAlways("x");
        second.ThenAlways("x");

        var run = await Run(services, graph);

        Assert.Equal(RunState.Faulted, run.State);
        Assert.Equal(0, first.AnswerCount);
    }

    /// <summary>Two critics are refused for the same reason.</summary>
    [Fact]
    public async Task TwoCriticsAreRefused()
    {
        using var services = TestServices.Create();
        var (graph, node, first, second) = Wire();

        node.FirstRole = DebateRole.Criticize;
        node.SecondRole = DebateRole.Criticize;
        first.ThenAlways("x");
        second.ThenAlways("x");

        Assert.Equal(RunState.Faulted, (await Run(services, graph)).State);
    }

    /// <summary>Two models both set to debate is the ordinary case and is allowed.</summary>
    [Fact]
    public async Task TwoDebatersAreAllowed()
    {
        using var services = TestServices.Create();
        var (graph, node, first, second) = Wire();

        node.FirstRole = DebateRole.Debate;
        node.SecondRole = DebateRole.Debate;
        first.ThenAlways("same thing said the same way");
        second.ThenAlways("same thing said the same way");

        Assert.Equal(RunState.Completed, (await Run(services, graph)).State);
    }

    /// <summary>A debate with only one model wired refuses rather than arguing with itself.</summary>
    [Fact]
    public async Task ADebateNeedsTwoModels()
    {
        using var services = TestServices.Create();
        var graph = new GraphModel();

        var node = new DebateNode();
        var only = new ScriptedModelNode("model a").ThenAlways("x");
        var subject = new RecordingNode("subject", new List<string>()) { Append = "something" };

        graph.AddNode(node);
        graph.AddNode(only);
        graph.AddNode(subject);
        graph.TryConnect(subject.Out, node.Subject, out _);
        graph.TryConnect(only.Self, node.FirstModel, out _);

        Assert.Equal(RunState.Faulted, (await Run(services, graph)).State);
    }

    /// <summary>A debate with nothing to argue about refuses rather than inventing a subject.</summary>
    [Fact]
    public async Task ADebateNeedsASubject()
    {
        using var services = TestServices.Create();
        var graph = new GraphModel();

        var node = new DebateNode();
        var first = new ScriptedModelNode("model a").ThenAlways("x");
        var second = new ScriptedModelNode("model b").ThenAlways("x");

        graph.AddNode(node);
        graph.AddNode(first);
        graph.AddNode(second);
        graph.TryConnect(first.Self, node.FirstModel, out _);
        graph.TryConnect(second.Self, node.SecondModel, out _);

        Assert.Equal(RunState.Faulted, (await Run(services, graph)).State);
    }

    /// <summary>A time budget that is not a time is refused before anything is asked.</summary>
    [Fact]
    public async Task AnUnreadableBudgetIsRefused()
    {
        using var services = TestServices.Create();
        var (graph, node, first, second) = Wire();

        node.TimeBudget = "whenever";
        first.ThenAlways("x");
        second.ThenAlways("x");

        Assert.Equal(RunState.Faulted, (await Run(services, graph)).State);
        Assert.Equal(0, first.AnswerCount);
        Assert.False(node.BudgetIsValid);
    }

    [Theory]
    [InlineData("05:00", true)]
    [InlineData("0:30", true)]
    [InlineData("", false)]
    [InlineData("five minutes", false)]
    public void ABudgetIsReadAsATime(string budget, bool valid)
        => Assert.Equal(valid, new DebateNode { TimeBudget = budget }.BudgetIsValid);

    /// <summary>Every threshold has a sentence saying what it means, including both extremes.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(70)]
    [InlineData(90)]
    [InlineData(100)]
    public void EveryThresholdExplainsItself(int threshold)
        => Assert.False(string.IsNullOrWhiteSpace(new DebateNode { ConvergenceThreshold = threshold }.ThresholdMeaning));
}

/// <summary>
/// Reading a model's own account of how much it agrees.
/// </summary>
/// <remarks>
/// One of the two ways convergence is measured. It is the model's own number, which is worth
/// having and is not worth trusting alone, which is why the other way exists.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class AgreementReadingTests
{
    [Fact]
    public void ANumberAtTheEndIsRead()
        => Assert.Equal(80, DebateJudge.ReadAgreement("I mostly agree.\n\nAGREEMENT: 80"));

    /// <summary>
    /// The last number wins, because a model quoting the instruction back is common.
    /// </summary>
    /// <remarks>
    /// A reply that opens by restating "end with AGREEMENT: 0 to 100" and then answers would be
    /// read as total disagreement if the first match were taken, and the debate would run to its
    /// cap for a reason that has nothing to do with the debate.
    /// </remarks>
    [Fact]
    public void TheLastNumberWins()
        => Assert.Equal(90, DebateJudge.ReadAgreement("You asked me to end with AGREEMENT: 0.\n\nAGREEMENT: 90"));

    /// <summary>An unreadable reply is a missing measurement, not a zero.</summary>
    [Fact]
    public void NoNumberIsNotZero()
    {
        Assert.Null(DebateJudge.ReadAgreement("I have no idea."));
        Assert.Null(DebateJudge.ReadAgreement(string.Empty));
    }

    /// <summary>A number outside the range is brought back into it rather than refused.</summary>
    [Fact]
    public void ANumberIsClampedToTheRange()
        => Assert.Equal(100, DebateJudge.ReadAgreement("AGREEMENT: 400"));
}

/// <summary>
/// A model reading two positions and settling them, in each of the three modes.
/// </summary>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class JudgeTests
{
    private static (GraphModel Graph, JudgeNode Node, ScriptedModelNode Judge) Wire(
        JudgeMode mode,
        string? first = "the first position",
        string? second = null)
    {
        var graph = new GraphModel();

        var node = new JudgeNode { Mode = mode };
        var judge = new ScriptedModelNode("judge").ThenAlways("the verdict");

        graph.AddNode(node);
        graph.AddNode(judge);
        Assert.True(graph.TryConnect(judge.Self, node.Judge, out _));

        if (first is not null)
        {
            var a = new RecordingNode("a", new List<string>()) { Append = first };
            graph.AddNode(a);
            Assert.True(graph.TryConnect(a.Out, node.First, out _));
        }

        if (second is not null)
        {
            var b = new RecordingNode("b", new List<string>()) { Append = second };
            graph.AddNode(b);
            Assert.True(graph.TryConnect(b.Out, node.Second, out _));
        }

        return (graph, node, judge);
    }

    [Theory]
    [InlineData(JudgeMode.DecideIndependently)]
    [InlineData(JudgeMode.ChooseASide)]
    [InlineData(JudgeMode.Combine)]
    public async Task EveryModeProducesAVerdict(JudgeMode mode)
    {
        using var services = TestServices.Create();
        var (graph, node, judge) = Wire(mode, "position one", "position two");

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal("the verdict", node.LastVerdict);
        Assert.Equal(1, judge.AnswerCount);
    }

    /// <summary>
    /// Each mode tells the judge something different, which is the whole of the difference.
    /// </summary>
    /// <remarks>
    /// The judge logic is written once and the mode only changes what it is told, so the thing
    /// worth asserting is that the three instructions are actually three.
    /// </remarks>
    [Fact]
    public async Task TheModesSayDifferentThings()
    {
        var said = new List<string>();

        foreach (var mode in new[] { JudgeMode.DecideIndependently, JudgeMode.ChooseASide, JudgeMode.Combine })
        {
            using var services = TestServices.Create();
            var (graph, _, judge) = Wire(mode, "position one", "position two");

            await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

            said.Add(judge.Asked[0]);
        }

        Assert.Equal(3, said.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>One position is a read on whether what arrived stands up, not an error.</summary>
    [Fact]
    public async Task OnePositionIsStillJudged()
    {
        using var services = TestServices.Create();
        var (graph, node, judge) = Wire(JudgeMode.DecideIndependently, "the only position");

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal(1, judge.AnswerCount);
    }

    /// <summary>A judge with no model refuses.</summary>
    [Fact]
    public async Task AJudgeNeedsAModel()
    {
        using var services = TestServices.Create();

        var graph = new GraphModel();
        var node = new JudgeNode();
        var a = new RecordingNode("a", new List<string>()) { Append = "something" };

        graph.AddNode(node);
        graph.AddNode(a);
        graph.TryConnect(a.Out, node.First, out _);

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Faulted, run.State);
    }

    /// <summary>A judge with nothing to judge refuses.</summary>
    [Fact]
    public async Task AJudgeNeedsSomethingToJudge()
    {
        using var services = TestServices.Create();
        var (graph, _, judge) = Wire(JudgeMode.Combine, first: null);

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Faulted, run.State);
        Assert.Equal(0, judge.AnswerCount);
    }

    /// <summary>A model that says it cannot answer is not asked anyway.</summary>
    [Fact]
    public async Task AModelThatCannotAnswerIsNotAsked()
    {
        using var services = TestServices.Create();
        var (graph, _, judge) = Wire(JudgeMode.Combine, "something");

        judge.Unavailable = "no endpoint is configured";

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Faulted, run.State);
        Assert.Equal(0, judge.AnswerCount);
    }
}
