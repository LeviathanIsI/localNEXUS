namespace LocalNEXUS.App.Services.History;

/// <summary>What became of a file a run meant to write.</summary>
public enum FileOutcome
{
    /// <summary>It is on disk.</summary>
    Written,

    /// <summary>It is waiting, because it did not compile.</summary>
    Staged,

    /// <summary>A project rule refused it. It compiles; writing it would have broken something.</summary>
    Refused
}

/// <summary>One past run, as the list shows it.</summary>
/// <param name="RunId">Its identity, which the rest of the record hangs off.</param>
/// <param name="StartedAt">When it began.</param>
/// <param name="EndedAt">When it finished, or null for a run that never reported an end.</param>
/// <param name="State">The run state it ended in.</param>
/// <param name="Request">What was typed.</param>
/// <param name="NodeCount">How many nodes the graph had.</param>
/// <param name="Cost">What it spent, in dollars.</param>
/// <param name="Written">How many files it wrote.</param>
/// <param name="Staged">How many it left waiting.</param>
/// <param name="Restorable">How many files it snapshotted, which is what undo can put back.</param>
public sealed record RunSummary(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string State,
    string Request,
    int NodeCount,
    decimal Cost,
    int Written,
    int Staged,
    int Restorable)
{
    /// <summary>The request on one line, for a row that has to fit.</summary>
    public string RequestLine
    {
        get
        {
            var flat = Request.ReplaceLineEndings(" ").Trim();

            if (flat.Length == 0)
            {
                return "No request text";
            }

            return flat.Length <= 120 ? flat : flat[..120] + "...";
        }
    }

    /// <summary>How long it took, or nothing when it never reported an end.</summary>
    public string Duration
    {
        get
        {
            if (EndedAt is not { } ended)
            {
                return "unfinished";
            }

            var span = ended - StartedAt;

            return span.TotalSeconds < 1
                ? $"{span.TotalMilliseconds:0} ms"
                : span.TotalMinutes < 1
                    ? $"{span.TotalSeconds:0.0} s"
                    : $"{span.TotalMinutes:0.0} min";
        }
    }

    /// <summary>What it produced, in one phrase.</summary>
    public string Produced => (Written, Staged) switch
    {
        (0, 0) => "nothing written",
        (_, 0) => $"{Written} file(s) written",
        (0, _) => $"{Staged} file(s) waiting",
        _ => $"{Written} written, {Staged} waiting"
    };

    /// <summary>True when there is something for undo to put back.</summary>
    public bool CanUndo => Restorable > 0;
}

/// <summary>One line of a run's transcript, read back from disk.</summary>
/// <param name="At">When it was recorded.</param>
/// <param name="Kind">The activity kind it was recorded under.</param>
/// <param name="Title">The headline.</param>
/// <param name="Detail">Everything else, which for a compile check is the diagnostics.</param>
public sealed record RunEventRecord(DateTimeOffset At, string Kind, string Title, string? Detail)
{
    /// <summary>The time on its own, which is what the transcript shows down its left edge.</summary>
    public string Time => At.ToString("HH:mm:ss");
}

/// <summary>One file a run dealt with.</summary>
/// <param name="Path">Where it was, relative to the project.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="Detail">The change it made, or why it did not happen.</param>
public sealed record RunFileRecord(string Path, FileOutcome Outcome, string? Detail);

/// <summary>A hit from a search over the whole history.</summary>
/// <param name="RunId">Which run it was found in.</param>
/// <param name="StartedAt">When that run was.</param>
/// <param name="Request">What that run was asked to do.</param>
/// <param name="Snippet">The matching text, with the match marked by the database.</param>
public sealed record HistoryHit(string RunId, DateTimeOffset StartedAt, string Request, string Snippet);

/// <summary>What the history is costing on disk.</summary>
/// <param name="Runs">How many runs are recorded.</param>
/// <param name="Snapshots">How many file snapshots are kept.</param>
/// <param name="SnapshotBytes">What those snapshots hold.</param>
/// <param name="DatabaseBytes">The size of the database file itself.</param>
public sealed record HistoryUsage(int Runs, int Snapshots, long SnapshotBytes, long DatabaseBytes)
{
    /// <summary>An empty history, for before one has been opened.</summary>
    public static HistoryUsage None { get; } = new(0, 0, 0, 0);

    /// <summary>The database, in the units a person reads.</summary>
    public string DatabaseText => Format(DatabaseBytes);

    /// <summary>The snapshots, in the units a person reads.</summary>
    public string SnapshotText => Format(SnapshotBytes);

    /// <summary>One line for the settings panel.</summary>
    public string Summary => Runs == 0
        ? "Nothing recorded for this project yet."
        : $"{Runs} run(s) recorded, {Format(DatabaseBytes)} of history and {Snapshots} snapshot(s) holding {Format(SnapshotBytes)}.";

    private static string Format(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} bytes",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB"
    };
}

/// <summary>What an undo did, said plainly enough to be reported.</summary>
/// <param name="Restored">Files put back to what they held before.</param>
/// <param name="Removed">Files deleted, because the run created them and they were not there before.</param>
/// <param name="Failed">Files that could not be put back, each with why.</param>
public sealed record UndoOutcome(int Restored, int Removed, IReadOnlyList<string> Failed)
{
    /// <summary>True when every file went back.</summary>
    public bool Complete => Failed.Count == 0;

    /// <summary>One line for the feed.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();

            if (Restored > 0)
            {
                parts.Add($"{Restored} file(s) put back");
            }

            if (Removed > 0)
            {
                parts.Add($"{Removed} file(s) removed");
            }

            if (Failed.Count > 0)
            {
                parts.Add($"{Failed.Count} could not be undone");
            }

            return parts.Count == 0 ? "Nothing to undo" : string.Join(", ", parts);
        }
    }
}
