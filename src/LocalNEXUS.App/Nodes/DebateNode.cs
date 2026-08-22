using System.Globalization;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Debate;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Puts two models in genuine disagreement about how to approach something, and emits what they
/// settled on as a brief.
/// </summary>
/// <remarks>
/// This is the first node in the application that uses more than one model at once, which is the
/// point of the application: getting more out of several models than any one of them gives you.
///
/// It argues about approach and emits a prompt, never code. That is what makes it affordable. Two
/// models arguing over a paragraph costs a fraction of two models arguing over three hundred
/// lines, and what a model does when it has to defend a position, which is expose the assumptions
/// it was quietly making, happens just as well on a paragraph.
///
/// The rounds live here, not in the graph. The executor orders nodes and rejects cycles, and it is
/// right to: a loop drawn on a canvas is a worse way to say "up to six times" than a number is.
/// This is the same arrangement as the tool loop inside a model node and the repair loop inside a
/// compiler check, and the executor learns nothing from any of them.
///
/// The pairing that matters is one model arguing from the open project and the other from what is
/// generally right. That is the real tension in most decisions about a codebase, and it needs the
/// project index.
/// </remarks>
public sealed partial class DebateNode : NodeBase
{
    /// <summary>
    /// The most rounds a debate will run, whatever the other limits say.
    /// </summary>
    /// <remarks>
    /// Six, and it is a constant rather than a setting because it is a guard rather than a
    /// preference. A fast pair of local models can get through twenty exchanges inside a generous
    /// time budget, and by the sixth round two models that have not come together are not going to.
    /// The threshold and the clock are what somebody tunes; this is what stops either of them
    /// being tuned into an accident.
    /// </remarks>
    public const int MaximumRounds = 6;

    /// <summary>The longest a debate may run for, so a typo cannot leave one going all night.</summary>
    public static readonly TimeSpan MaximumBudget = TimeSpan.FromMinutes(30);

    /// <summary>How far apart the positions may still be and count as settled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ThresholdMeaning))]
    private int _convergenceThreshold = 70;

    /// <summary>The clock, as it is typed on the node.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BudgetIsValid))]
    private string _timeBudget = "05:00";

    /// <summary>What happens when the rounds run out with the positions still apart.</summary>
    [ObservableProperty]
    private NonConvergence _onNotConverged = NonConvergence.FallBackToJudge;

    /// <summary>How the fallback judge resolves the two positions.</summary>
    [ObservableProperty]
    private JudgeMode _fallbackJudgeMode = JudgeMode.Combine;

    /// <summary>Which of the two models writes the final brief, and judges when one is needed.</summary>
    [ObservableProperty]
    private DebateArbiter _arbiter = DebateArbiter.Second;

    /// <summary>What the first model is doing.</summary>
    [ObservableProperty]
    private DebateRole _firstRole = DebateRole.Debate;

    /// <summary>What the first model argues from.</summary>
    [ObservableProperty]
    private DebateSource _firstSource = DebateSource.Codebase;

    /// <summary>What the second model is doing.</summary>
    [ObservableProperty]
    private DebateRole _secondRole = DebateRole.Debate;

    /// <summary>What the second model argues from.</summary>
    [ObservableProperty]
    private DebateSource _secondSource = DebateSource.OwnReasoning;

    /// <summary>How the last debate ended, for the node footer.</summary>
    [ObservableProperty]
    private string _lastOutcome = string.Empty;

    public DebateNode()
        : base("Debate")
    {
        Subject = AddInput("Text", PinType.Text);
        FirstModel = AddInput("Model A", PinType.Model);
        SecondModel = AddInput("Model B", PinType.Model);
        Brief = AddOutput("Text", PinType.Text);
    }

    /// <summary>What is being argued about. A request, a plan, a spec.</summary>
    /// <remarks>
    /// Text, and only text. A debate needs a subject, and a model is not one: wiring a model here
    /// would be asking two models to argue about a third, which is not a question anybody has.
    /// </remarks>
    public Pin Subject { get; }

    /// <summary>The first debater.</summary>
    public Pin FirstModel { get; }

    /// <summary>The second debater.</summary>
    public Pin SecondModel { get; }

    /// <summary>The brief the debate settled on, ready for a coder or a judge.</summary>
    public Pin Brief { get; }

    /// <inheritdoc />
    public override string TypeKey => "Debate";

    /// <summary>True when the clock on the node reads as a time.</summary>
    public bool BudgetIsValid => TryReadBudget(TimeBudget, out _);

    /// <summary>
    /// What the threshold currently means, in the terms somebody would actually think in.
    /// </summary>
    /// <remarks>
    /// Both ends of this slider are legitimate and neither is obvious, which is exactly when a
    /// number needs a sentence next to it. Zero is not "off", it is "one round", and a run that
    /// emits after one round has bought two answers in parallel rather than a debate.
    /// </remarks>
    public string ThresholdMeaning => ConvergenceThreshold switch
    {
        <= 0 => "Emits after one round. That is two models answering the same question in parallel, "
                + "which is a reasonable thing to want and is not a debate.",
        < 40 => "Settles as soon as the two are roughly pointing the same way. Fast, and it will "
                + "accept real disagreement about the details.",
        < 75 => "Settles when they broadly agree on the approach and differ on specifics. This is "
                + "the range where the rounds are doing something and still finish.",
        < 95 => "Demanding. Expect several rounds, and expect some debates to run out of rounds and "
                + "fall to whichever behaviour is set below.",
        _ => "Two models will not agree this closely. This will almost always run out of rounds or "
             + "clock, so what is set below is what will actually decide it."
    };

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var subject = ctx.GetText(Subject);

        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new InvalidOperationException(
                $"{Title} has nothing to argue about. Wire a request, a plan or a spec into its Text pin.");
        }

        var first = Require(ctx, FirstModel, "Model A");
        var second = Require(ctx, SecondModel, "Model B");

        EnforcePairing();

        if (!TryReadBudget(TimeBudget, out var budget))
        {
            throw new InvalidOperationException(
                $"{Title} could not read \"{TimeBudget}\" as a time. Enter it as minutes and seconds, for example 05:00.");
        }

        var firstContext = ctx.ForNode((NodeBase)first);
        var secondContext = ctx.ForNode((NodeBase)second);

        var arbiter = Arbiter == DebateArbiter.First ? first : second;
        var arbiterContext = Arbiter == DebateArbiter.First ? firstContext : secondContext;

        var projectContext = await BuildProjectContextAsync(ctx, subject, ct).ConfigureAwait(false);

        var firstGrounding = FirstSource == DebateSource.Codebase ? projectContext : string.Empty;
        var secondGrounding = SecondSource == DebateSource.Codebase ? projectContext : string.Empty;

        ctx.Feed.Info(
            $"{Title}: opening",
            $"{((NodeBase)first).Title} as {FirstRole} from {Describe(FirstSource)}, "
            + $"{((NodeBase)second).Title} as {SecondRole} from {Describe(SecondSource)}. "
            + $"Settles at {ConvergenceThreshold} percent, at most {MaximumRounds} rounds, at most {Format(budget)}.");

        var clock = System.Diagnostics.Stopwatch.StartNew();

        var firstPosition = await first
            .AnswerAsync(
                DebatePrompts.SystemFor(FirstRole, FirstSource),
                DebatePrompts.OpeningFor(subject, firstGrounding),
                firstContext,
                ct)
            .ConfigureAwait(false);

        var secondPosition = await second
            .AnswerAsync(
                DebatePrompts.SystemFor(SecondRole, SecondSource),
                DebatePrompts.OpeningFor(subject, secondGrounding),
                secondContext,
                ct)
            .ConfigureAwait(false);

        Record(ctx, 1, (NodeBase)first, firstPosition, null);
        Record(ctx, 1, (NodeBase)second, secondPosition, null);

        var scored = ConvergenceMeter.Measure(firstPosition, secondPosition);
        ReportConvergence(ctx, 1, scored, null, null);

        var round = 1;

        while (!Settled(scored) && round < MaximumRounds && clock.Elapsed < budget)
        {
            ct.ThrowIfCancellationRequested();
            round++;

            StatusMessage = $"Round {round} of at most {MaximumRounds}";

            var previousFirst = firstPosition;
            var previousSecond = secondPosition;

            firstPosition = await first
                .AnswerAsync(
                    DebatePrompts.SystemFor(FirstRole, FirstSource),
                    DebatePrompts.RoundFor(round, subject, previousSecond, firstGrounding),
                    firstContext,
                    ct)
                .ConfigureAwait(false);

            secondPosition = await second
                .AnswerAsync(
                    DebatePrompts.SystemFor(SecondRole, SecondSource),
                    DebatePrompts.RoundFor(round, subject, previousFirst, secondGrounding),
                    secondContext,
                    ct)
                .ConfigureAwait(false);

            var firstSelf = DebateJudge.ReadAgreement(firstPosition);
            var secondSelf = DebateJudge.ReadAgreement(secondPosition);

            Record(ctx, round, (NodeBase)first, firstPosition, firstSelf);
            Record(ctx, round, (NodeBase)second, secondPosition, secondSelf);

            scored = ConvergenceMeter.Measure(firstPosition, secondPosition);
            ReportConvergence(ctx, round, scored, firstSelf, secondSelf);
        }

        clock.Stop();

        var converged = Settled(scored);

        if (!converged)
        {
            var resolved = await ResolveAsync(
                    ctx, arbiter, arbiterContext, subject, firstPosition, secondPosition, round, scored, clock.Elapsed, budget, ct)
                .ConfigureAwait(false);

            if (resolved is { } verdict)
            {
                    LastOutcome = scored.IsMeasured
                        ? $"Judged after {round} round(s) at {scored.Text}"
                        : $"Judged after {round} round(s), never measurable";
                StatusMessage = LastOutcome;
                return NodeResult.FromPin(Brief, verdict);
            }
        }

        // The summary is written by the arbiter, which has read both positions from outside all
        // the way through and is the one model in the room with no side to state.
        var brief = await arbiter
            .AnswerAsync(
                DebatePrompts.SummarySystem,
                DebatePrompts.SummaryMessage(subject, firstPosition, secondPosition),
                arbiterContext,
                ct)
            .ConfigureAwait(false);

        LastOutcome = converged
            ? $"Settled after {round} round(s) at {scored.Text}"
            : scored.IsMeasured
                ? $"Ran out after {round} round(s) at {scored.Text}, and went on anyway"
                : $"Ran out after {round} round(s) without a measurable score, and went on anyway";

        StatusMessage = LastOutcome;
        ctx.Feed.Add(ActivityKind.NodeCompleted, $"{Title}: {LastOutcome}", brief, Id);

        return NodeResult.FromPin(Brief, brief);
    }

    /// <summary>
    /// What to do about two positions that did not come together.
    /// </summary>
    /// <returns>A verdict when a judge decided, or null when the debate should summarise itself.</returns>
    /// <remarks>
    /// Waiting is a node awaiting a task the interface completes, which is what asking a question
    /// has been since v0.4 and what triage does when it cannot plan. Nothing above this node is
    /// involved and there is no resuming, because nothing stopped.
    /// </remarks>
    private async Task<string?> ResolveAsync(
        NodeExecutionContext ctx,
        IModelHandle arbiter,
        NodeExecutionContext arbiterContext,
        string subject,
        string firstPosition,
        string secondPosition,
        int rounds,
        Convergence scored,
        TimeSpan elapsed,
        TimeSpan budget,
        CancellationToken ct)
    {
        var why = elapsed >= budget
            ? $"the clock ran out after {Format(elapsed)}"
            : $"they used all {rounds} round(s)";

        if (OnNotConverged == NonConvergence.FallBackToJudge)
        {
            ctx.Feed.Info(
                $"{Title}: a judge is deciding",
                $"The two positions are at {scored.Explanation} and {why}. "
                + $"{((NodeBase)arbiter).Title} will {Describe(FallbackJudgeMode)}.");

            return await DebateJudge
                .DecideAsync(arbiter, arbiterContext, FallbackJudgeMode, subject, firstPosition, secondPosition, ct)
                .ConfigureAwait(false);
        }

        var question =
            $"{Title} could not get its two models to agree. They are at {scored.Explanation} and {why}."
            + $"{Environment.NewLine}{Environment.NewLine}"
            + $"Say which way to go, or anything that would settle it, and the debate will take it as the "
            + $"deciding word. Proceed without answering and a judge will decide instead.";

        var outcome = ctx.Services.Conversation is not { } conversation
            ? Services.History.ClarificationOutcome.Unanswered
            : await conversation
                .AskAsync(question, ctx.RunId, Services.History.ConversationService.AnswerTimeout, ct)
                .ConfigureAwait(false);

        if (!outcome.Answered)
        {
            ctx.Feed.Info(
                $"{Title}: nobody answered, so a judge decided",
                $"{((NodeBase)arbiter).Title} will {Describe(FallbackJudgeMode)}.");

            return await DebateJudge
                .DecideAsync(arbiter, arbiterContext, FallbackJudgeMode, subject, firstPosition, secondPosition, ct)
                .ConfigureAwait(false);
        }

        ctx.Feed.Info($"{Title}: settled by hand", outcome.Text);

        // What somebody typed is the deciding word, so it goes to the judge as a third position
        // rather than replacing the brief outright. The brief still has to be usable downstream.
        return await DebateJudge
            .DecideAsync(
                arbiter,
                arbiterContext,
                JudgeMode.Combine,
                $"{subject}{Environment.NewLine}{Environment.NewLine}The deciding word from the person running this: {outcome.Text}",
                firstPosition,
                secondPosition,
                ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Refuses a pairing that cannot produce a disagreement.
    /// </summary>
    /// <remarks>
    /// Both debating is allowed, because two models each arguing what they believe will differ.
    /// Two defenders never disagree and two critics never propose anything, so both of those are
    /// refused rather than run: a debate that cannot produce tension costs two models and produces
    /// one position with extra steps.
    /// </remarks>
    private void EnforcePairing()
    {
        if (FirstRole != SecondRole || FirstRole == DebateRole.Debate)
        {
            return;
        }

        throw new InvalidOperationException(FirstRole == DebateRole.Defend
            ? $"{Title} has both models set to defend, and two defenders never disagree. Set one to "
              + "criticize, or set both to debate."
            : $"{Title} has both models set to criticize, and two critics never propose anything. Set "
              + "one to defend, or set both to debate.");
    }

    /// <summary>What the project already contains, for whichever model argues from it.</summary>
    /// <remarks>
    /// Built once even when both models want it, and skipped entirely when neither does. With no
    /// project open this is empty rather than an error: a debate about approach is perfectly
    /// possible without one, and the model told to argue from the codebase is told plainly that
    /// there is none rather than being left to invent it.
    /// </remarks>
    private async Task<string> BuildProjectContextAsync(NodeExecutionContext ctx, string subject, CancellationToken ct)
    {
        if (FirstSource != DebateSource.Codebase && SecondSource != DebateSource.Codebase)
        {
            return string.Empty;
        }

        var project = ctx.Services.UnityProject;

        if (!project.HasProject)
        {
            ctx.Feed.Info(
                $"{Title}: no project to argue from",
                "A model is set to argue from the codebase and none is open, so it argues from what it knows.");

            return string.Empty;
        }

        var index = ctx.Services.ProjectIndex;
        await index.EnsureAsync(project.ProjectPath, new DelegateProgress<string>(m => StatusMessage = m), ct)
            .ConfigureAwait(false);

        var budget = new ContextBudget();
        var candidates = RelevanceRanker.Rank(index, subject, budget.CandidateLimit);

        var map = ProjectDigest.BuildMap(index, candidates, budget);
        var summary = ProjectDigest.BuildCandidateSummary(candidates, budget);

        return summary.Length == 0 ? map : $"{map}{Environment.NewLine}{Environment.NewLine}{summary}";
    }

    private bool Settled(Convergence scored) => scored.Score is { } value && value >= ConvergenceThreshold;

    private static IModelHandle Require(NodeExecutionContext ctx, Pin pin, string which)
        => ctx.GetSourceNode(pin) as IModelHandle
           ?? throw new InvalidOperationException(
               $"Debate needs a model on {which}. Wire a Model node's Model output into it.");

    /// <summary>
    /// Writes one turn to the feed, which is what puts it in the record.
    /// </summary>
    /// <remarks>
    /// No second store. Everything written to the feed during a run is already appended to that
    /// run's rows by the recorder, so the transcript is readable in the panel while it happens and
    /// in the history window afterwards, from the same rows. Two models arguing about somebody's
    /// architecture is the most interesting thing this application produces, and it would be a
    /// waste to keep only the verdict.
    /// </remarks>
    private void Record(NodeExecutionContext ctx, int round, NodeBase model, string position, int? selfReported)
    {
        var said = selfReported is { } value ? $" It puts itself at {value} percent." : string.Empty;

        ctx.Feed.Add(
            ActivityKind.ModelStream,
            $"{Title}: round {round}, {model.Title}",
            $"{position}{said}",
            Id);
    }

    private void ReportConvergence(
        NodeExecutionContext ctx,
        int round,
        Convergence scored,
        int? firstSelf,
        int? secondSelf)
    {
        // Both measurements, side by side, because the gap between them is the interesting number.
        // Two models each claiming ninety while a count of what they actually named says forty
        // means they are being agreeable rather than agreeing, and only the measured one gates.
        var selves = firstSelf is null && secondSelf is null
            ? "Neither said how far it had come."
            : $"They put themselves at {Describe(firstSelf)} and {Describe(secondSelf)}.";

        ctx.Feed.Info(
            $"{Title}: after round {round}, {(scored.IsMeasured ? $"measured at {scored.Text}" : "not measurable")}",
            $"{selves} {(scored.IsMeasured
                ? $"The measured number is what decides, and the threshold is {ConvergenceThreshold} percent."
                : $"There was too little in common to judge: {scored.Reason}. Nothing settles on an "
                  + "unmeasured round, and it is not being read as disagreement.")}"
            + $"{Environment.NewLine}{Environment.NewLine}{scored.Breakdown()}");
    }

    private static string Describe(int? value) => value is { } v ? $"{v} percent" : "not measured";

    private static string Describe(DebateSource source)
        => source == DebateSource.Codebase ? "the codebase" : "its own reasoning";

    private static string Describe(JudgeMode mode) => mode switch
    {
        JudgeMode.ChooseASide => "choose a side",
        JudgeMode.Combine => "combine them",
        _ => "decide independently"
    };

    /// <summary>Reads mm:ss, and refuses anything past the ceiling.</summary>
    public static bool TryReadBudget(string text, out TimeSpan budget)
    {
        budget = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Split(':');

        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            || minutes < 0
            || seconds is < 0 or > 59)
        {
            return false;
        }

        budget = new TimeSpan(0, minutes, seconds);

        return budget > TimeSpan.Zero && budget <= MaximumBudget;
    }

    private static string Format(TimeSpan span) => $"{(int)span.TotalMinutes:00}:{span.Seconds:00}";

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["convergenceThreshold"] = ConvergenceThreshold,
        ["timeBudget"] = TimeBudget,
        ["onNotConverged"] = OnNotConverged.ToString(),
        ["fallbackJudgeMode"] = FallbackJudgeMode.ToString(),
        ["arbiter"] = Arbiter.ToString(),
        ["firstRole"] = FirstRole.ToString(),
        ["firstSource"] = FirstSource.ToString(),
        ["secondRole"] = SecondRole.ToString(),
        ["secondSource"] = SecondSource.ToString()
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        ConvergenceThreshold = Math.Clamp(settings["convergenceThreshold"]?.GetValue<int>() ?? 70, 0, 100);
        TimeBudget = settings["timeBudget"]?.GetValue<string>() ?? "05:00";

        OnNotConverged = Read(settings, "onNotConverged", NonConvergence.FallBackToJudge);
        FallbackJudgeMode = Read(settings, "fallbackJudgeMode", JudgeMode.Combine);
        Arbiter = Read(settings, "arbiter", DebateArbiter.Second);
        FirstRole = Read(settings, "firstRole", DebateRole.Debate);
        FirstSource = Read(settings, "firstSource", DebateSource.Codebase);
        SecondRole = Read(settings, "secondRole", DebateRole.Debate);
        SecondSource = Read(settings, "secondSource", DebateSource.OwnReasoning);
    }

    private static T Read<T>(JsonObject settings, string key, T fallback)
        where T : struct, Enum
        => Enum.TryParse<T>(settings[key]?.GetValue<string>(), out var value) ? value : fallback;

    partial void OnConvergenceThresholdChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, 100);

        if (clamped != value)
        {
            ConvergenceThreshold = clamped;
        }
    }
}
