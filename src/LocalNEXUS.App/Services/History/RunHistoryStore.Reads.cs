using System.IO;
using Microsoft.Data.Sqlite;

namespace LocalNEXUS.App.Services.History;

/// <summary>
/// The read side: everything the history window asks, answered by query rather than from memory.
/// </summary>
/// <remarks>
/// Every one of these opens its own connection and closes it. That is deliberate and it is what
/// write ahead logging is for: a read never blocks the writer and never shares its connection, so
/// the history window can be open, and searching, while a run is still appending to the same file.
/// </remarks>
public sealed partial class RunHistoryStore
{
    /// <summary>The most recent runs, newest first.</summary>
    public async Task<IReadOnlyList<RunSummary>> ListRunsAsync(int limit, CancellationToken ct)
    {
        return await ReadAsync(
            """
            SELECT r.run_id, r.started_at, r.ended_at, r.state, r.request, r.node_count, r.cost,
                   r.written, r.staged,
                   (SELECT COUNT(*) FROM snapshots s WHERE s.run_id = r.run_id)
            FROM runs r
            ORDER BY r.started_at DESC
            LIMIT $limit;
            """,
            reader => new RunSummary(
                reader.GetString(0),
                DateTimeOffset.Parse(reader.GetString(1)),
                reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                (decimal)reader.GetDouble(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9)),
            ct,
            ("$limit", limit)).ConfigureAwait(false);
    }

    /// <summary>One run's transcript, in the order it happened.</summary>
    public async Task<IReadOnlyList<RunEventRecord>> ReadEventsAsync(string runId, CancellationToken ct)
    {
        return await ReadAsync(
            "SELECT at, kind, title, detail FROM events WHERE run_id = $id ORDER BY id;",
            reader => new RunEventRecord(
                DateTimeOffset.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3)),
            ct,
            ("$id", runId)).ConfigureAwait(false);
    }

    /// <summary>What one run did to files.</summary>
    public async Task<IReadOnlyList<RunFileRecord>> ReadFilesAsync(string runId, CancellationToken ct)
    {
        return await ReadAsync(
            "SELECT path, outcome, detail FROM files WHERE run_id = $id ORDER BY id;",
            reader => new RunFileRecord(
                reader.GetString(0),
                Enum.TryParse<FileOutcome>(reader.GetString(1), out var outcome) ? outcome : FileOutcome.Written,
                reader.IsDBNull(2) ? null : reader.GetString(2)),
            ct,
            ("$id", runId)).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds text anywhere in the record.
    /// </summary>
    /// <remarks>
    /// This is what replaces summarising old context. If something is not in front of you, it is
    /// on disk and can be pulled back in the words it was written in, rather than as somebody's
    /// precis of them. Answering from what happens to be in view when the record is right here is
    /// the failure this exists to prevent.
    ///
    /// Keyword matching is the known limit. A search for the word somebody used finds it; a search
    /// for a different word meaning the same thing does not. That is the trade for costing nothing
    /// and needing no model, and it is where a semantic layer would attach if one were ever wanted.
    /// </remarks>
    public async Task<IReadOnlyList<HistoryHit>> SearchAsync(string query, int limit, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<HistoryHit>();
        }

        try
        {
            return await ReadAsync(
                """
                WITH hits AS (
                    SELECT run_id,
                           snippet(search, 3, '[', ']', ' ... ', 24) AS body,
                           rank AS score
                    FROM search
                    WHERE search MATCH $query
                    ORDER BY rank
                    LIMIT $scan
                )
                SELECT h.run_id, r.started_at, r.request, h.body, MIN(h.score)
                FROM hits h
                JOIN runs r ON r.run_id = h.run_id
                GROUP BY h.run_id
                ORDER BY MIN(h.score)
                LIMIT $limit;
                """,
                reader => new HistoryHit(
                    reader.GetString(0),
                    DateTimeOffset.Parse(reader.GetString(1)),
                    reader.GetString(2),
                    reader.GetString(3)),
                ct,
                ("$query", Quote(query)),
                ("$scan", limit * ScanFactor),
                ("$limit", limit)).ConfigureAwait(false);
        }
        catch (SqliteException ex)
        {
            // Not swallowed. This used to return an empty list on the grounds that a query the
            // matcher will not accept is a typo, which is true and was also how a broken query
            // hid: every search this application has ever run failed here and reported nothing
            // found. Whatever the reason, the caller is told the difference between a search that
            // ran and matched nothing and a search that did not run.
            throw new HistoryQueryException(
                $"That search could not be run against this project's history: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// How many matching events are considered before they are folded down to one row per run.
    /// </summary>
    /// <remarks>
    /// A run writes many events and a search over a busy one can match several of them, so taking
    /// exactly the asked for number of events would return fewer runs than asked for. This takes a
    /// few times as many and lets the grouping decide.
    /// </remarks>
    private const int ScanFactor = 8;

    /// <summary>
    /// Wraps a search in quotes so that what somebody typed is looked for rather than parsed.
    /// </summary>
    /// <remarks>
    /// The matcher has an expression syntax of its own, and a colon or a stray bracket in an
    /// ordinary sentence is a syntax error in it. Treating the whole thing as a phrase is what a
    /// search box is expected to do.
    /// </remarks>
    private static string Quote(string query) => "\"" + query.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>What the record is costing on disk.</summary>
    public async Task<HistoryUsage> ReadUsageAsync(CancellationToken ct)
    {
        var path = DatabasePath;

        if (path is null)
        {
            return HistoryUsage.None;
        }

        var rows = await ReadAsync(
            "SELECT (SELECT COUNT(*) FROM runs), (SELECT COUNT(*) FROM snapshots), (SELECT COALESCE(SUM(bytes), 0) FROM snapshots);",
            reader => (Runs: reader.GetInt32(0), Snapshots: reader.GetInt32(1), Bytes: reader.GetInt64(2)),
            ct).ConfigureAwait(false);

        var first = rows.Count > 0 ? rows[0] : (Runs: 0, Snapshots: 0, Bytes: 0L);

        long databaseBytes = 0;
        try
        {
            var info = new FileInfo(path);
            databaseBytes = info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            databaseBytes = 0;
        }

        return new HistoryUsage(first.Runs, first.Snapshots, first.Bytes, databaseBytes);
    }

    /// <summary>
    /// Puts back every file a run wrote or changed.
    /// </summary>
    /// <remarks>
    /// This is run undo, not version control, and the difference is not a detail. It knows only
    /// what this application itself wrote down before touching a file. Anything Unity rewrote, an
    /// extension changed or a person edited by hand happened outside its view, and putting a file
    /// back to what it held before the run also puts back whatever was done to it since.
    ///
    /// The request is left alone. Wanting the files back is not the same as wanting to un ask the
    /// question, and somebody who reverts a bad attempt usually wants to try again.
    /// </remarks>
    public async Task<UndoOutcome> UndoAsync(string runId, CancellationToken ct)
    {
        var snapshots = await ReadAsync(
            "SELECT absolute_path, existed, content FROM snapshots WHERE run_id = $id ORDER BY id;",
            reader => (
                Path: reader.GetString(0),
                Existed: reader.GetInt32(1) == 1,
                Content: reader.IsDBNull(2) ? null : reader.GetString(2)),
            ct,
            ("$id", runId)).ConfigureAwait(false);

        var restored = 0;
        var removed = 0;
        var failed = new List<string>();

        foreach (var snapshot in snapshots)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (!snapshot.Existed)
                {
                    if (File.Exists(snapshot.Path))
                    {
                        File.Delete(snapshot.Path);

                        // The meta file goes with it. Leaving one behind for a script that is no
                        // longer there is exactly the litter Unity complains about on next import.
                        var meta = snapshot.Path + ".meta";

                        if (File.Exists(meta))
                        {
                            File.Delete(meta);
                        }

                        removed++;
                    }

                    continue;
                }

                if (snapshot.Content is null)
                {
                    failed.Add($"{snapshot.Path} was too large to keep a copy of, so it cannot be put back.");
                    continue;
                }

                // Written in place, never deleted and recreated, for the same reason every other
                // write in this application is: recreating a script issues a new meta guid and
                // unbinds every scene that used it.
                await File.WriteAllTextAsync(snapshot.Path, snapshot.Content, ct).ConfigureAwait(false);
                restored++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add($"{snapshot.Path}: {ex.Message}");
            }
        }

        return new UndoOutcome(restored, removed, failed);
    }

    /// <summary>
    /// Drops snapshots past the count and age allowed, leaving the text of the runs alone.
    /// </summary>
    /// <remarks>
    /// Text is small and snapshots are not, so only snapshots are capped. A run whose snapshots
    /// have been pruned keeps its whole transcript and simply can no longer be undone, which is
    /// the right thing to lose first.
    /// </remarks>
    public void PruneSnapshots(int keepRuns, int maximumAgeDays)
    {
        var cutoff = DateTimeOffset.Now.AddDays(-Math.Max(1, maximumAgeDays)).ToString("O");
        var keep = Math.Max(1, keepRuns);

        Enqueue(connection =>
        {
            Execute(connection, "DELETE FROM snapshots WHERE captured_at < $cutoff;", ("$cutoff", cutoff));

            Execute(
                connection,
                """
                DELETE FROM snapshots WHERE run_id NOT IN (
                    SELECT run_id FROM runs ORDER BY started_at DESC LIMIT $keep);
                """,
                ("$keep", keep));
        });
    }

    /// <summary>Forgets every snapshot, keeping the record of what happened.</summary>
    public void ClearSnapshots() => Enqueue(connection => Execute(connection, "DELETE FROM snapshots;"));

    /// <summary>Forgets everything: the runs, their transcripts, the files and the snapshots.</summary>
    public void ClearHistory() => Enqueue(connection =>
    {
        Execute(connection, "DELETE FROM snapshots;");
        Execute(connection, "DELETE FROM turns;");
        Execute(connection, "DELETE FROM files;");
        Execute(connection, "DELETE FROM events;");
        Execute(connection, "DELETE FROM search;");
        Execute(connection, "DELETE FROM runs;");
        Execute(connection, "VACUUM;");
    });

    private async Task<IReadOnlyList<T>> ReadAsync<T>(
        string sql,
        Func<SqliteDataReader, T> project,
        CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        var path = DatabasePath;

        if (path is null)
        {
            return Array.Empty<T>();
        }

        var rows = new List<T>();

        await using var connection = Connect(path);
        await connection.OpenAsync(ct).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(project(reader));
        }

        return rows;
    }
}
