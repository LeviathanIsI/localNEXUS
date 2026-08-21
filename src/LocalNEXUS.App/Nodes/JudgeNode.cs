using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Debate;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Reads two positions and makes a determination.
/// </summary>
/// <remarks>
/// The same logic a debate falls back to when its models will not agree, invoked the other way.
/// It is written once, in <see cref="DebateJudge"/>, and this node is one of its two callers.
///
/// Wiring one of these is what asks for a third opinion however well the debate went. That is not
/// a setting on the debate node, because it is a fact about the graph rather than about the
/// debate: behaviour comes from the canvas, configuration comes from the node, which is how the
/// rest of this application is arranged.
///
/// It also works without a debate in front of it. Two ordinary model nodes feeding a judge is a
/// plain second opinion with none of the rounds, which is a cheaper thing to want and is a
/// perfectly good reason to have this node.
/// </remarks>
public sealed partial class JudgeNode : NodeBase
{
    /// <summary>How this judge resolves what it is given.</summary>
    /// <remarks>
    /// Deciding independently is the default. Somebody who wires a judge deliberately is asking
    /// for a determination rather than an arbitration: choosing a side throws away half the
    /// reasoning that was just paid for, and combining tends to produce a position neither model
    /// would defend. Both are still here because both are sometimes what is wanted, and the
    /// fallback inside a debate defaults to combining for exactly the opposite reason.
    /// </remarks>
    [ObservableProperty]
    private JudgeMode _mode = JudgeMode.DecideIndependently;

    /// <summary>What the judge decided last, for the node footer.</summary>
    [ObservableProperty]
    private string _lastVerdict = string.Empty;

    public JudgeNode()
        : base("Judge")
    {
        First = AddInput("Text", PinType.Text);
        Second = AddInput("Second", PinType.Text);
        Judge = AddInput("Model", PinType.Model);
        Verdict = AddOutput("Text", PinType.Text);
    }

    /// <summary>
    /// What is being judged. A brief from a debate, or one of two positions.
    /// </summary>
    public Pin First { get; }

    /// <summary>
    /// A second position, when there is one.
    /// </summary>
    /// <remarks>
    /// Optional, and that is what lets this sit downstream of a debate as well as between two
    /// model nodes. A debate has already resolved two positions into one brief, so there is
    /// nothing to put here; two models arguing separately give one each.
    /// </remarks>
    public Pin Second { get; }

    /// <summary>The model doing the judging.</summary>
    public Pin Judge { get; }

    /// <summary>The determination, in the same shape a debate emits, so it drops into the same place.</summary>
    public Pin Verdict { get; }

    /// <inheritdoc />
    public override string TypeKey => "Judge";

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var first = ctx.GetText(First);

        if (string.IsNullOrWhiteSpace(first))
        {
            throw new InvalidOperationException(
                $"{Title} has nothing to judge. Wire a debate, or a model, into its Text pin.");
        }

        if (ctx.GetSourceNode(Judge) is not IModelHandle judge)
        {
            throw new InvalidOperationException(
                $"{Title} has no model to judge with. Wire a Model node's Model output into its Model input.");
        }

        if (!judge.CanAnswer(out var whyNot))
        {
            throw new InvalidOperationException($"{Title} cannot judge: {whyNot}");
        }

        var second = ctx.GetText(Second);
        var judgeNode = (NodeBase)judge;

        // One position is a judge reading a settled brief and saying whether it stands, which is
        // what sitting downstream of a debate means. Two is the plain second opinion.
        var alone = string.IsNullOrWhiteSpace(second);

        ctx.Feed.Info(
            $"{Title}: {judgeNode.Title} is {Describe(Mode)}",
            alone
                ? "One position, so this is a read on whether what arrived stands up."
                : "Two positions, so this is a determination between them.");

        StatusMessage = Describe(Mode);

        var verdict = await DebateJudge
            .DecideAsync(
                judge,
                ctx.ForNode(judgeNode),
                Mode,
                alone
                    ? "Judge whether the position below stands up, and write the brief that should be acted on."
                    : "Decide between the two positions below.",
                first,
                alone ? "There is no second position. Judge the first on its own merits." : second,
                ct)
            .ConfigureAwait(false);

        LastVerdict = verdict;
        StatusMessage = $"{Describe(Mode)}, {verdict.Length} characters";

        ctx.Feed.Add(ActivityKind.NodeCompleted, $"{Title}: {Describe(Mode)}", verdict, Id);

        return NodeResult.FromPin(Verdict, verdict);
    }

    private static string Describe(JudgeMode mode) => mode switch
    {
        JudgeMode.ChooseASide => "choosing a side",
        JudgeMode.Combine => "combining both",
        _ => "deciding independently"
    };

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["mode"] = Mode.ToString()
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
        => Mode = Enum.TryParse<JudgeMode>(settings["mode"]?.GetValue<string>(), out var mode)
            ? mode
            : JudgeMode.DecideIndependently;
}
