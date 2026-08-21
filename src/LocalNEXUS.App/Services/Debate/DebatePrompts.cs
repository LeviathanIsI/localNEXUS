using System.Text;

namespace LocalNEXUS.App.Services.Debate;

/// <summary>
/// What the models are told, and how the question is framed to them.
/// </summary>
/// <remarks>
/// The framing is even handed and says so out loud. Two models set against each other for sport
/// produce posturing, and posturing is worse than a single pass: it costs two calls and buys
/// rhetoric. What is wanted is the thing a model does when it has to defend a position, which is
/// expose the assumptions it was quietly making. So every prompt here says the same three things.
///
/// They are resolving a question, not winning one. Changing position when the other is right is
/// the correct outcome and is stated as such, so that agreeing does not read as losing.
///
/// Claims are about the code, not about the other model. Nothing addresses the opponent, nothing
/// scores points, and nothing is asked to be persuasive.
///
/// Brevity is required. A debate about approach is a paragraph each, not an essay, and that is
/// what makes this cheap: two models arguing over a paragraph costs a fraction of two models
/// arguing over three hundred lines.
/// </remarks>
public static class DebatePrompts
{
    /// <summary>The shared framing every debater is given, whatever its role.</summary>
    private const string CommonFraming =
        "You are one of two engineers resolving a technical question. You are not competing. "
        + "The goal is the best approach, not your approach, and saying that the other position is "
        + "right about something is the correct move when it is. "
        + "Argue about the code and never about the other engineer. "
        + "Be specific: name types, files and trade offs rather than describing qualities. "
        + "Be brief: a few short paragraphs at most. You are deciding an approach, not writing it.";

    /// <summary>The system prompt for one debater.</summary>
    public static string SystemFor(DebateRole role, DebateSource source)
    {
        var hat = role switch
        {
            DebateRole.Defend =>
                "Your job is to make the strongest case for the proposal on the table and to answer "
                + "what is said against it. Concede a point that cannot be answered rather than talking past it.",

            DebateRole.Criticize =>
                "Your job is to find where the proposal breaks: what it assumes, what it costs later, "
                + "what it does not handle. Attack the proposal, never the person. If it survives your "
                + "best objection, say so.",

            _ => "Your job is to argue for the approach you actually believe is right, and to change "
                 + "your position when the other one is better."
        };

        var grounding = source == DebateSource.Codebase
            ? "Argue from what this project already does. You are shown what it contains; prefer what "
              + "fits it, and say which existing type or file each claim rests on."
            : "Argue from what you know about the problem in general. You are deliberately not shown "
              + "this project's contents, so do not guess at them.";

        return $"{CommonFraming} {hat} {grounding}";
    }

    /// <summary>The opening message for a debater, before anything has been said.</summary>
    public static string OpeningFor(string subject, string projectContext)
    {
        var builder = new StringBuilder();

        builder.AppendLine("The question is how to approach this:");
        builder.AppendLine();
        builder.AppendLine(subject.Trim());

        if (projectContext.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("What this project already contains:");
            builder.AppendLine();
            builder.AppendLine(projectContext);
        }

        builder.AppendLine();
        builder.AppendLine("State the approach you would take and why, in a few short paragraphs.");

        return builder.ToString();
    }

    /// <summary>
    /// The message for a later round, carrying what the other model actually said.
    /// </summary>
    /// <remarks>
    /// The other position is quoted in full rather than summarised. A round where each model reads
    /// a precis of the other is not a debate, it is two monologues with a lossy channel between
    /// them, and the whole reason for doing this in rounds is that a model reasons differently when
    /// it has to answer something specific.
    /// </remarks>
    public static string RoundFor(int round, string subject, string otherPosition, string projectContext)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"Round {round}. The question is still:");
        builder.AppendLine();
        builder.AppendLine(subject.Trim());

        if (projectContext.Length > 0)
        {
            builder.AppendLine();
            builder.AppendLine("What this project already contains:");
            builder.AppendLine();
            builder.AppendLine(projectContext);
        }

        builder.AppendLine();
        builder.AppendLine("The other engineer said:");
        builder.AppendLine();
        builder.AppendLine(otherPosition.Trim());
        builder.AppendLine();
        builder.AppendLine("Answer the specific points. Say where they are right and change your position "
                           + "where they have convinced you. Then state where you still differ and why.");
        builder.AppendLine();
        builder.AppendLine("End with one line exactly in this form, and nothing after it:");
        builder.AppendLine("AGREEMENT: <a whole number from 0 to 100, how far you now agree with the other position>");

        return builder.ToString();
    }

    /// <summary>The system prompt for the outside read on how far apart two positions are.</summary>
    public const string ScorerSystem =
        "You read two engineering positions and report how far apart they are. You take no side and "
        + "you write nothing but the number asked for. You are measuring substance, not tone: two "
        + "positions that are polite about each other while proposing different architectures are far "
        + "apart, and two that argue about wording while proposing the same thing are close.";

    /// <summary>The message asking for that number.</summary>
    public static string ScorerMessage(string first, string second)
    {
        var builder = new StringBuilder();

        builder.AppendLine("First position:");
        builder.AppendLine();
        builder.AppendLine(first.Trim());
        builder.AppendLine();
        builder.AppendLine("Second position:");
        builder.AppendLine();
        builder.AppendLine(second.Trim());
        builder.AppendLine();
        builder.AppendLine("How much do these two agree on the approach to take? Answer with one line "
                           + "and nothing else, in this form:");
        builder.AppendLine("AGREEMENT: <a whole number from 0 to 100>");

        return builder.ToString();
    }

    /// <summary>The system prompt for turning a settled debate into something a coder can use.</summary>
    public const string SummarySystem =
        "You turn a resolved technical discussion into a brief for whoever implements it. You write "
        + "the approach that was agreed, the reasoning that got there, and a restatement of what was "
        + "originally asked for. You write no code. You write nothing about the discussion itself, "
        + "who said what, or how much they agreed.";

    /// <summary>The message asking for that brief.</summary>
    public static string SummaryMessage(string subject, string first, string second)
    {
        var builder = new StringBuilder();

        builder.AppendLine("What was originally asked for:");
        builder.AppendLine();
        builder.AppendLine(subject.Trim());
        builder.AppendLine();
        builder.AppendLine("Where the first engineer ended up:");
        builder.AppendLine();
        builder.AppendLine(first.Trim());
        builder.AppendLine();
        builder.AppendLine("Where the second ended up:");
        builder.AppendLine();
        builder.AppendLine(second.Trim());
        builder.AppendLine();
        builder.AppendLine("Write the brief. Three parts, in this order and with these headings:");
        builder.AppendLine();
        builder.AppendLine("APPROACH");
        builder.AppendLine("What to do, specifically enough to act on.");
        builder.AppendLine();
        builder.AppendLine("WHY");
        builder.AppendLine("The reasoning, including anything that was ruled out and what ruled it out.");
        builder.AppendLine();
        builder.AppendLine("REQUEST");
        builder.AppendLine("A short restatement of what was asked for, so this brief stands on its own.");

        return builder.ToString();
    }
}
