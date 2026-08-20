namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// How much of a file changed, counted in lines added and lines removed.
/// </summary>
/// <remarks>
/// Not a diff, and deliberately not one. What the feed needs is the size of a change, so that a
/// three line fix and a rewrite of the whole file do not look identical in the transcript, and a
/// count of lines answers that at a fraction of the cost of computing an edit script.
///
/// Counted as a multiset difference: a line that appears in both versions the same number of
/// times is unchanged wherever it moved to, so reordering a file does not report as a rewrite,
/// while a line that appears once more than before counts as one addition. Blank lines are
/// ignored, because a reformat that only changes spacing is not a change worth a number.
/// </remarks>
/// <param name="Added">Lines present afterwards that were not present before.</param>
/// <param name="Removed">Lines present before that are not present afterwards.</param>
public readonly record struct DiffStat(int Added, int Removed)
{
    /// <summary>A file that is new, so every line of it is an addition.</summary>
    public static DiffStat ForNewFile(string content) => new(CountLines(content), 0);

    /// <summary>True when there is anything to report.</summary>
    public bool HasChange => Added > 0 || Removed > 0;

    /// <summary>The counts as the feed shows them, for example <c>+34 -6</c>.</summary>
    public string Text => $"+{Added} -{Removed}";

    /// <summary>Counts what changed between two versions of a file.</summary>
    public static DiffStat Between(string? original, string updated)
    {
        if (original is null)
        {
            return ForNewFile(updated);
        }

        var before = Tally(original);
        var after = Tally(updated);

        var added = 0;
        var removed = 0;

        foreach (var (line, count) in after)
        {
            var was = before.GetValueOrDefault(line);
            if (count > was)
            {
                added += count - was;
            }
        }

        foreach (var (line, count) in before)
        {
            var now = after.GetValueOrDefault(line);
            if (count > now)
            {
                removed += count - now;
            }
        }

        return new DiffStat(added, removed);
    }

    private static Dictionary<string, int> Tally(string content)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var line in Lines(content))
        {
            counts[line] = counts.GetValueOrDefault(line) + 1;
        }

        return counts;
    }

    private static int CountLines(string content) => Lines(content).Count();

    private static IEnumerable<string> Lines(string content) => content
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 0);
}
