using System.Text;
using LocalNEXUS.App.Services.History;

namespace LocalNEXUS.App.Services.Planning;

/// <summary>
/// Reads a questions section out of a planner reply, and refuses the ones nobody could answer.
/// </summary>
/// <remarks>
/// The filtering here is the second half of the bar the prompt sets, and it is the half that
/// holds. A prompt can ask a model not to be chatty; it cannot make it so. A question that names
/// fewer than two concrete alternatives is not a fork, it is a request for reassurance, and those
/// are dropped without being shown to anybody. If every question is dropped, there was no real
/// gap and the run plans instead, which is exactly what it did before this existed.
///
/// The whole point is that asking has to be rarer than guessing was expensive. A tool that
/// interrupts once a week to prevent a wasted run is worth having; one that interrupts every run
/// is worse than the guessing it replaced.
/// </remarks>
public static class ClarificationParser
{
    /// <summary>The heading the planner uses when it is asking rather than planning.</summary>
    private const string Heading = "QUESTIONS";

    /// <summary>
    /// The most questions worth putting to somebody at once.
    /// </summary>
    /// <remarks>
    /// A series is the point: three unresolved things asked together beat three round trips. Past
    /// this it stops being a series and becomes a form, and a model producing that many has
    /// misunderstood the request rather than found that many forks.
    /// </remarks>
    public const int MaximumQuestions = 4;

    /// <summary>
    /// Pulls the answerable questions out of a reply, or nothing when it is a plan.
    /// </summary>
    public static IReadOnlyList<ClarificationQuestion> Parse(string reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return Array.Empty<ClarificationQuestion>();
        }

        var lines = reply.ReplaceLineEndings("\n").Split('\n');
        var inside = false;
        var questions = new List<ClarificationQuestion>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith(Heading, StringComparison.OrdinalIgnoreCase))
            {
                inside = true;
                continue;
            }

            // A reply that has both sections is answering the wrong way round. The plan wins,
            // because a plan is an answer and a question is a request for one.
            if (line.StartsWith("PLAN", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("DECISIONS", StringComparison.OrdinalIgnoreCase))
            {
                return Array.Empty<ClarificationQuestion>();
            }

            if (!inside)
            {
                continue;
            }

            var parts = line.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 3)
            {
                // Fewer than a question and two options. Dropped rather than shown.
                continue;
            }

            var question = new ClarificationQuestion(parts[0], parts.Skip(1).ToList());

            if (question.IsAnswerable)
            {
                questions.Add(question);
            }

            if (questions.Count == MaximumQuestions)
            {
                break;
            }
        }

        return questions;
    }

    /// <summary>
    /// The questions as they appear in the chat: numbered, with their alternatives under them.
    /// </summary>
    public static string Format(IReadOnlyList<ClarificationQuestion> questions)
    {
        var builder = new StringBuilder();

        builder.AppendLine(questions.Count == 1
            ? "One thing has to be decided before this can be planned."
            : $"{questions.Count} things have to be decided before this can be planned.");

        builder.AppendLine();

        for (var i = 0; i < questions.Count; i++)
        {
            builder.AppendLine($"{i + 1}. {questions[i].Text}");

            foreach (var option in questions[i].Options)
            {
                builder.AppendLine($"   - {option}");
            }

            builder.AppendLine();
        }

        builder.Append(questions.Count == 1
            ? "Answer below, or press proceed and it will choose the first option."
            : "Answer all of them below in one message, or press proceed and it will choose the first option for each.");

        return builder.ToString();
    }

    /// <summary>
    /// What the run assumed when nobody answered.
    /// </summary>
    /// <remarks>
    /// The first option, and said out loud. An assumption nobody can see is the failure this whole
    /// feature exists to prevent: the point of asking was that a confident wrong answer costs a
    /// full run to discover, and an unstated default is a confident wrong answer with extra steps.
    /// </remarks>
    public static string DescribeAssumption(IReadOnlyList<ClarificationQuestion> questions)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Nobody answered, so these were assumed:");

        foreach (var question in questions)
        {
            builder.AppendLine($"- {question.Text} Assumed: {question.Options[0]}");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>The answers as they go back to the planner.</summary>
    public static string DescribeAnswers(IReadOnlyList<ClarificationQuestion> questions, string answer)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You asked, and were told:");

        foreach (var question in questions)
        {
            builder.AppendLine($"- {question}");
        }

        builder.AppendLine();
        builder.AppendLine("The answer was:");
        builder.AppendLine(answer.Trim());
        builder.AppendLine();
        builder.AppendLine("Plan now. Do not ask again.");

        return builder.ToString();
    }
}
