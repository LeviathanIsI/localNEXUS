using System.Text.RegularExpressions;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Services.Planning;

/// <summary>
/// Reads the planner's reply back into decisions and a file plan.
/// </summary>
/// <remarks>
/// Line oriented and pipe delimited because that is what small local models produce reliably.
/// JSON is the obvious alternative and the wrong one here: a seven billion parameter model asked
/// for JSON will emit a trailing comma or an unescaped quote often enough to matter, and a
/// half parsed plan is worse than a plan in a format that cannot really go wrong.
///
/// Every part of the parse is tolerant. Markdown fences, bullets, numbering, extra whitespace and
/// missing sections are all normal, and a row that cannot be read is skipped rather than taking
/// the plan down with it. What is not tolerated is inventing content: a reply with no readable
/// plan rows produces an empty plan, and the caller says so.
/// </remarks>
public static class PlanParser
{
    private static readonly Regex FencePattern = new(@"^\s*```[A-Za-z0-9#+_-]*\s*$", RegexOptions.Compiled);
    private static readonly Regex LeadingBullet = new(@"^\s*(?:[-*>]\s*)?(?:\d+[.)]\s*)?", RegexOptions.Compiled);

    /// <summary>The two section headings the planner is asked for.</summary>
    private const string DecisionsHeading = "DECISIONS";

    private const string PlanHeading = "PLAN";

    /// <summary>What the parser managed to read out of a reply.</summary>
    /// <param name="Verdicts">The decision rows.</param>
    /// <param name="Rows">The plan rows, in the order they were written.</param>
    public readonly record struct ParsedPlan(IReadOnlyList<CandidateVerdict> Verdicts, IReadOnlyList<PlanRow> Rows);

    /// <summary>One row of the plan, before it becomes a task with context attached.</summary>
    /// <param name="Operation">Create or edit.</param>
    /// <param name="RelativePath">Where the file goes.</param>
    /// <param name="TypeName">The main type it declares.</param>
    /// <param name="Intent">What it is for.</param>
    public readonly record struct PlanRow(FileOperation Operation, string RelativePath, string TypeName, string Intent);

    /// <summary>Reads a reply. Never throws: an unreadable reply is an empty result.</summary>
    public static ParsedPlan Parse(string? reply)
    {
        var verdicts = new List<CandidateVerdict>();
        var rows = new List<PlanRow>();

        if (string.IsNullOrWhiteSpace(reply))
        {
            return new ParsedPlan(verdicts, rows);
        }

        var section = Section.None;

        foreach (var raw in reply.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();

            if (line.Length == 0 || FencePattern.IsMatch(line))
            {
                continue;
            }

            var heading = HeadingOf(line);

            if (heading != Section.None)
            {
                section = heading;
                continue;
            }

            var body = LeadingBullet.Replace(line, string.Empty).Trim();

            if (!body.Contains('|'))
            {
                continue;
            }

            switch (section)
            {
                case Section.Decisions when TryReadVerdict(body, out var verdict):
                    verdicts.Add(verdict);
                    break;

                case Section.Plan when TryReadRow(body, out var row):
                    rows.Add(row);
                    break;

                case Section.None when TryReadRow(body, out var loose):
                    // A reply that skipped the headings but wrote usable rows is still a plan.
                    rows.Add(loose);
                    break;
            }
        }

        return new ParsedPlan(verdicts, rows);
    }

    private static Section HeadingOf(string line)
    {
        var bare = line.Trim('#', '*', ' ', ':').Trim();

        if (bare.Equals(DecisionsHeading, StringComparison.OrdinalIgnoreCase))
        {
            return Section.Decisions;
        }

        return bare.Equals(PlanHeading, StringComparison.OrdinalIgnoreCase) ? Section.Plan : Section.None;
    }

    /// <summary>
    /// Reads a decision row: path, decision, and a reason. The decision may carry a symbol, as in
    /// CREATE_NEW_REFERENCING PlayerHealth.
    /// </summary>
    private static bool TryReadVerdict(string line, out CandidateVerdict verdict)
    {
        verdict = default!;

        var parts = Split(line);

        if (parts.Count < 2)
        {
            return false;
        }

        var path = ProjectIndexService.Normalise(parts[0]);
        var words = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return false;
        }

        var decision = words[0].Replace("-", "_", StringComparison.Ordinal).ToUpperInvariant() switch
        {
            "USE_AS_IS" or "USE" or "USEASIS" => CandidateDecision.UseAsIs,
            "EDIT" => CandidateDecision.Edit,
            "CREATE_NEW_REFERENCING" or "CREATE_NEW" or "CREATE" => CandidateDecision.CreateNewReferencing,
            "IGNORE" or "SKIP" or "NOT_RELEVANT" => CandidateDecision.Ignore,
            _ => CandidateDecision.Undecided
        };

        if (decision == CandidateDecision.Undecided)
        {
            return false;
        }

        var symbol = words.Length > 1 ? string.Join(' ', words[1..]).Trim() : null;
        var reason = parts.Count > 2 ? parts[2] : string.Empty;

        verdict = new CandidateVerdict(path, decision, string.IsNullOrWhiteSpace(symbol) ? null : symbol, reason);
        return true;
    }

    /// <summary>
    /// Reads a plan row. The leading order number is optional because it is implied by position,
    /// and models drop it about as often as they include it.
    /// </summary>
    private static bool TryReadRow(string line, out PlanRow row)
    {
        row = default;

        var parts = Split(line);

        if (parts.Count > 0 && int.TryParse(parts[0], out _))
        {
            parts.RemoveAt(0);
        }

        if (parts.Count < 2)
        {
            return false;
        }

        var operation = parts[0].Trim().ToUpperInvariant() switch
        {
            "CREATE" or "NEW" or "ADD" => FileOperation.Create,
            "EDIT" or "MODIFY" or "CHANGE" or "UPDATE" => FileOperation.Edit,
            _ => (FileOperation?)null
        };

        if (operation is null)
        {
            return false;
        }

        var path = ProjectIndexService.Normalise(parts[1]);

        if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var typeName = parts.Count > 2 && parts[2].Length > 0
            ? parts[2]
            : System.IO.Path.GetFileNameWithoutExtension(path);

        var intent = parts.Count > 3 ? parts[3] : string.Empty;

        row = new PlanRow(operation.Value, path, typeName, intent);
        return true;
    }

    private static List<string> Split(string line)
        => line.Split('|').Select(p => p.Trim().Trim('`').Trim()).ToList();

    private enum Section
    {
        None,
        Decisions,
        Plan
    }
}
