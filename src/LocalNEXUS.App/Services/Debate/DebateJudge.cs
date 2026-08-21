using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.Services.Debate;

/// <summary>
/// Deciding between two positions, and reading how far apart they are.
/// </summary>
/// <remarks>
/// One implementation, two ways in. A judge is configured inside a debate as what happens when the
/// rounds run out, and wired on the canvas as a node when somebody wants a third opinion however
/// well the models agreed. Those are the same operation invoked from different places, and writing
/// it twice would mean two things that drift.
///
/// Which of those two applies is not a setting. Wiring a Judge node is what asks for the always
/// case, which is consistent with the rest of the application: behaviour comes from the canvas,
/// configuration comes from the node.
/// </remarks>
public static class DebateJudge
{
    /// <summary>The line every scored answer is expected to end with.</summary>
    private static readonly Regex Agreement = new(
        @"AGREEMENT\s*:\s*(\d{1,3})",
        RegexOptions.IgnoreCase,
        TimeSpan.FromSeconds(2));

    /// <summary>
    /// Reads a number out of a reply that was asked to end with one.
    /// </summary>
    /// <remarks>
    /// The last match rather than the first, because a model quoting the instruction back before
    /// answering it is common and the answer is always the last thing said. Nothing found is a
    /// missing measurement rather than a zero: reporting an unparsed reply as total disagreement
    /// would keep a debate running for reasons that have nothing to do with the debate.
    /// </remarks>
    public static int? ReadAgreement(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return null;
        }

        try
        {
            var matches = Agreement.Matches(reply);

            if (matches.Count == 0)
            {
                return null;
            }

            var text = matches[^1].Groups[1].Value;

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? Math.Clamp(value, 0, 100)
                : null;
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>
    /// Asks a model how far apart two positions are.
    /// </summary>
    /// <returns>A number from 0 to 100, or null when the reply could not be read as one.</returns>
    public static async Task<int?> ScoreAsync(
        IModelHandle arbiter,
        NodeExecutionContext arbiterContext,
        string first,
        string second,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(arbiter);

        var reply = await arbiter
            .AnswerAsync(DebatePrompts.ScorerSystem, DebatePrompts.ScorerMessage(first, second), arbiterContext, ct)
            .ConfigureAwait(false);

        return ReadAgreement(reply);
    }

    /// <summary>
    /// Resolves two positions into one brief, in whichever way was asked for.
    /// </summary>
    /// <remarks>
    /// The output is the same shape whatever the mode: the brief a coder is handed. That is what
    /// makes a judge droppable into the same place a debate's own output goes, and what lets one
    /// be wired downstream of the other without anything changing further along.
    /// </remarks>
    public static async Task<string> DecideAsync(
        IModelHandle judge,
        NodeExecutionContext judgeContext,
        JudgeMode mode,
        string subject,
        string first,
        string second,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(judge);

        return await judge
            .AnswerAsync(SystemFor(mode), MessageFor(mode, subject, first, second), judgeContext, ct)
            .ConfigureAwait(false);
    }

    /// <summary>What the judge is told it is for.</summary>
    private static string SystemFor(JudgeMode mode)
    {
        var hat = mode switch
        {
            JudgeMode.ChooseASide =>
                "You pick the better of two positions and write it up. You do not merge them and you "
                + "do not invent a third. Say which you chose and what decided it.",

            JudgeMode.Combine =>
                "You merge two positions into one that keeps what is right in each. Where they cannot "
                + "both be true, choose, and say what decided it. Do not produce something neither "
                + "engineer would defend.",

            _ => "You read two positions and then write your own, informed by both and bound to "
                 + "neither. Where you agree with one, say so. Where you think both are wrong, say that "
                 + "and give the reasoning."
        };

        return $"{hat} You write no code. Your answer is a brief for whoever implements it.";
    }

    /// <summary>What the judge is asked, with the same three headings a debate produces.</summary>
    private static string MessageFor(JudgeMode mode, string subject, string first, string second)
    {
        var builder = new StringBuilder();

        builder.AppendLine("What was asked for:");
        builder.AppendLine();
        builder.AppendLine(subject.Trim());
        builder.AppendLine();
        builder.AppendLine("First position:");
        builder.AppendLine();
        builder.AppendLine(first.Trim());
        builder.AppendLine();
        builder.AppendLine("Second position:");
        builder.AppendLine();
        builder.AppendLine(second.Trim());
        builder.AppendLine();

        builder.AppendLine(mode switch
        {
            JudgeMode.ChooseASide => "Choose one of these two and write it up.",
            JudgeMode.Combine => "Merge these into one position and write it up.",
            _ => "Write your own determination, informed by both."
        });

        builder.AppendLine();
        builder.AppendLine("Three parts, in this order and with these headings:");
        builder.AppendLine();
        builder.AppendLine("APPROACH");
        builder.AppendLine("What to do, specifically enough to act on.");
        builder.AppendLine();
        builder.AppendLine("WHY");
        builder.AppendLine("The reasoning, including what was ruled out and what ruled it out.");
        builder.AppendLine();
        builder.AppendLine("REQUEST");
        builder.AppendLine("A short restatement of what was asked for, so this stands on its own.");

        return builder.ToString();
    }
}
