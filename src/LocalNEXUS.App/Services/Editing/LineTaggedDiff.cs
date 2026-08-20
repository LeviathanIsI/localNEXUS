using System.Text;

namespace LocalNEXUS.App.Services.Editing;

/// <summary>
/// Parses and applies the line tagged diff format, tolerantly.
/// </summary>
/// <remarks>
/// The format is a stripped down unified diff: blocks introduced by <c>@@</c>, whose lines are
/// prefixed with a space to keep, a minus to remove and a plus to add. Line numbers are
/// deliberately absent, because a model asked to count lines gets it wrong and a block located by
/// its content does not need them.
///
/// Applying is layered, because the failure that actually happens is never a wrong idea, it is a
/// reproduced line with different trailing whitespace or a tab where there were spaces. So the
/// block is looked for exactly, then ignoring trailing whitespace, then ignoring leading
/// whitespace as well. Only when all three miss is it a failure, and the message then says which
/// line could not be found so the repair loop has something to act on.
/// </remarks>
public static class LineTaggedDiff
{
    /// <summary>One block of change: what must be there, and what it becomes.</summary>
    /// <param name="Before">The lines expected in the file, context and removals in order.</param>
    /// <param name="After">The lines that replace them, context and additions in order.</param>
    public readonly record struct Hunk(IReadOnlyList<string> Before, IReadOnlyList<string> After);

    /// <summary>True when a reply looks like this format rather than a whole file.</summary>
    public static bool LooksLikeDiff(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return false;
        }

        foreach (var raw in reply.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.TrimStart().StartsWith("@@", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the blocks out of a reply. Anything before the first block is prose the model added
    /// despite being asked not to, and is ignored.
    /// </summary>
    public static IReadOnlyList<Hunk> Parse(string reply)
    {
        var hunks = new List<Hunk>();

        var before = new List<string>();
        var after = new List<string>();
        var started = false;

        void Flush()
        {
            if (started && (before.Count > 0 || after.Count > 0))
            {
                hunks.Add(new Hunk(before.ToList(), after.ToList()));
            }

            before.Clear();
            after.Clear();
        }

        foreach (var raw in (reply ?? string.Empty).Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith("@@", StringComparison.Ordinal))
            {
                Flush();
                started = true;
                continue;
            }

            if (!started)
            {
                continue;
            }

            // A fence inside the reply ends nothing; it is simply not part of the diff.
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Length == 0)
            {
                before.Add(string.Empty);
                after.Add(string.Empty);
                continue;
            }

            switch (line[0])
            {
                case '-':
                    before.Add(line[1..]);
                    break;

                case '+':
                    after.Add(line[1..]);
                    break;

                case ' ':
                    before.Add(line[1..]);
                    after.Add(line[1..]);
                    break;

                default:
                    // An untagged line is almost always a context line the model forgot to prefix.
                    before.Add(line);
                    after.Add(line);
                    break;
            }
        }

        Flush();

        return hunks;
    }

    /// <summary>
    /// Applies every block to a file, in order.
    /// </summary>
    /// <exception cref="EditApplyException">A block could not be located in the file.</exception>
    public static string Apply(string original, IReadOnlyList<Hunk> hunks)
    {
        ArgumentNullException.ThrowIfNull(hunks);

        if (hunks.Count == 0)
        {
            throw new EditApplyException("The reply contained no change blocks, so there was nothing to apply.");
        }

        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = SplitLines(original);

        var searchFrom = 0;

        foreach (var hunk in hunks)
        {
            var before = Trim(hunk.Before);
            var after = Trim(hunk.After);

            if (before.Count == 0)
            {
                // Nothing to match against, so the block is an insertion. Appending is the only
                // honest reading of it, and it is said out loud rather than guessed at silently.
                lines.AddRange(after);
                searchFrom = lines.Count;
                continue;
            }

            var at = Locate(lines, before, searchFrom);

            if (at < 0 && searchFrom > 0)
            {
                at = Locate(lines, before, 0);
            }

            if (at < 0)
            {
                throw new EditApplyException(BuildMissMessage(before));
            }

            lines.RemoveRange(at, before.Count);
            lines.InsertRange(at, after);
            searchFrom = at + after.Count;
        }

        return string.Join(newline, lines);
    }

    /// <summary>
    /// Finds a block of lines, trying three increasingly forgiving comparisons before giving up.
    /// </summary>
    private static int Locate(List<string> lines, IReadOnlyList<string> block, int from)
    {
        foreach (var comparison in new Func<string, string, bool>[] { ExactMatch, TrailingInsensitiveMatch, WhitespaceInsensitiveMatch })
        {
            for (var start = Math.Max(0, from); start + block.Count <= lines.Count; start++)
            {
                var matched = true;

                for (var i = 0; i < block.Count; i++)
                {
                    if (!comparison(lines[start + i], block[i]))
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return start;
                }
            }
        }

        return -1;
    }

    private static bool ExactMatch(string line, string expected)
        => string.Equals(line, expected, StringComparison.Ordinal);

    private static bool TrailingInsensitiveMatch(string line, string expected)
        => string.Equals(line.TrimEnd(), expected.TrimEnd(), StringComparison.Ordinal);

    private static bool WhitespaceInsensitiveMatch(string line, string expected)
        => string.Equals(Squash(line), Squash(expected), StringComparison.Ordinal);

    /// <summary>
    /// A line with its indentation and its runs of inner whitespace collapsed, which is what
    /// makes a tab for four spaces stop mattering.
    /// </summary>
    private static string Squash(string line)
    {
        var builder = new StringBuilder(line.Length);
        var lastWasSpace = true;

        foreach (var c in line.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        return builder.ToString();
    }

    /// <summary>Drops blank lines from the ends of a block, which models add freely.</summary>
    private static List<string> Trim(IReadOnlyList<string> block)
    {
        var start = 0;
        var end = block.Count;

        while (start < end && block[start].Trim().Length == 0)
        {
            start++;
        }

        while (end > start && block[end - 1].Trim().Length == 0)
        {
            end--;
        }

        return block.Skip(start).Take(end - start).ToList();
    }

    private static string BuildMissMessage(IReadOnlyList<string> before)
    {
        var shown = string.Join(Environment.NewLine, before.Take(6));
        var more = before.Count > 6 ? $"{Environment.NewLine}... and {before.Count - 6} more line(s)" : string.Empty;

        return "A change block could not be found in the file. These lines were expected but are not there, "
               + $"even ignoring whitespace:{Environment.NewLine}{shown}{more}";
    }

    private static List<string> SplitLines(string text)
        => text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
}
