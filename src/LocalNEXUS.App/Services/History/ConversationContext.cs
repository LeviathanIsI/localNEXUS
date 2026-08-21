using System.Text;

namespace LocalNEXUS.App.Services.History;

/// <summary>
/// Assembles what a run is told about the conversation it is part of.
/// </summary>
/// <remarks>
/// Three things go in, and the choice of those three is the whole design.
///
/// The message just typed, verbatim and first, because it is the request.
///
/// The recent exchange, verbatim. This is what a follow up refers to. "No, use the existing slot
/// rather than a new one" is meaningless without the turn before it, and carrying the last few
/// turns is what makes correcting a run cost a sentence instead of a restatement.
///
/// Older turns that mention what is being asked about, retrieved and verbatim. Not a summary. The
/// thread is on disk and indexed, so anything that falls out of the recent window is findable
/// rather than lost, and pulling the real words back is strictly better than carrying somebody's
/// precis of them. Compressing a thread into a rolling digest is the mechanism that makes an
/// assistant claim not to know something that is sitting in its own transcript.
///
/// All three are capped, because context is finite and a conversation is not. What does not fit is
/// dropped oldest first and said out loud rather than quietly trimmed.
/// </remarks>
public static class ConversationContext
{
    /// <summary>How many recent turns are carried verbatim.</summary>
    public const int RecentTurns = 8;

    /// <summary>How much of the recent exchange is allowed to be.</summary>
    public const int RecentCharacters = 4000;

    /// <summary>How many older turns are worth pulling back.</summary>
    public const int RecalledTurns = 3;

    /// <summary>How much of that recall is allowed to be.</summary>
    public const int RecalledCharacters = 2000;

    /// <summary>
    /// Builds the section describing the conversation so far, or an empty string on the first
    /// message of a thread.
    /// </summary>
    /// <param name="turns">The thread as it stands, oldest first, including the new message.</param>
    /// <param name="recalled">Older turns the new message matched, already excluding the recent ones.</param>
    public static string Build(IReadOnlyList<ConversationTurn> turns, IReadOnlyList<ConversationTurn> recalled)
    {
        ArgumentNullException.ThrowIfNull(turns);
        ArgumentNullException.ThrowIfNull(recalled);

        var builder = new StringBuilder();

        if (recalled.Count > 0)
        {
            builder.AppendLine("Earlier in this conversation, found because this message mentions it:");

            foreach (var turn in Fit(recalled, RecalledCharacters))
            {
                builder.AppendLine(turn.ForPrompt);
            }

            builder.AppendLine();
        }

        var recent = Recent(turns);

        if (recent.Count > 0)
        {
            builder.AppendLine("The conversation so far:");

            foreach (var turn in Fit(recent, RecentCharacters))
            {
                builder.AppendLine(turn.ForPrompt);
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// The turns carried verbatim, which is everything but the message being sent.
    /// </summary>
    /// <remarks>
    /// The newest turn is left out because it is the request itself and is already at the top of
    /// what the run is given. Repeating it under a heading that says it is history would tell the
    /// model the thing it was just asked to do has already been discussed.
    /// </remarks>
    public static IReadOnlyList<ConversationTurn> Recent(IReadOnlyList<ConversationTurn> turns)
        => turns.Count <= 1
            ? Array.Empty<ConversationTurn>()
            : turns.Take(turns.Count - 1).TakeLast(RecentTurns).ToList();

    /// <summary>
    /// Trims a set of turns to a character budget, dropping the oldest first.
    /// </summary>
    /// <remarks>
    /// Whole turns are dropped rather than each one being cut short. Half a sentence from six
    /// exchanges ago is worse than nothing: it reads as context and carries none.
    /// </remarks>
    private static IReadOnlyList<ConversationTurn> Fit(IReadOnlyList<ConversationTurn> turns, int budget)
    {
        var kept = new List<ConversationTurn>();
        var used = 0;

        for (var i = turns.Count - 1; i >= 0; i--)
        {
            var cost = turns[i].ForPrompt.Length + Environment.NewLine.Length;

            if (used + cost > budget && kept.Count > 0)
            {
                break;
            }

            used += cost;
            kept.Add(turns[i]);
        }

        kept.Reverse();
        return kept;
    }
}
