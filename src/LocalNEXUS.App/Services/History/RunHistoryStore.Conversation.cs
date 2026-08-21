using Microsoft.Data.Sqlite;

namespace LocalNEXUS.App.Services.History;

/// <summary>
/// The conversation, kept in the same database as the runs it drives.
/// </summary>
/// <remarks>
/// Deliberately not a second store. A turn that starts a run carries that run's identity, so the
/// transcript and the record are two views of one thing: opening a run in the history window and
/// reading the message that caused it are the same row followed in two directions. A separate
/// conversation file would be a second copy of the same facts, free to drift from the first.
///
/// Starting fresh mints a new thread rather than deleting anything. A conversation that only grows
/// becomes a liability, and one that can only be reset by destroying the record trades one problem
/// for a worse one.
/// </remarks>
public sealed partial class RunHistoryStore
{
    /// <summary>How many turns the transcript shows at once.</summary>
    public const int TranscriptLimit = 200;

    /// <summary>Appends one thing said.</summary>
    public void AppendTurn(ConversationTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        Enqueue(connection =>
        {
            Execute(
                connection,
                "INSERT OR REPLACE INTO turns (turn_id, thread_id, role, text, at, run_id) "
                + "VALUES ($turn, $thread, $role, $text, $at, $run);",
                ("$turn", turn.Id),
                ("$thread", turn.ThreadId),
                ("$role", turn.Role.ToString()),
                ("$text", turn.Text),
                ("$at", turn.At.ToString("O")),
                ("$run", turn.RunId));

            // Indexed with everything else, which is what lets an older exchange be found and
            // pulled back in the words it was written in rather than as a summary of them.
            Execute(connection, "DELETE FROM search WHERE event_id = $turn;", ("$turn", turn.Id));
            Index(connection, turn.RunId ?? turn.ThreadId, turn.Id, $"turn.{turn.Role}", turn.Text);
        });
    }

    /// <summary>The turns of one thread, oldest first.</summary>
    public async Task<IReadOnlyList<ConversationTurn>> ReadTurnsAsync(string threadId, int limit, CancellationToken ct)
    {
        var rows = await ReadAsync(
            """
            SELECT turn_id, thread_id, role, text, at, run_id
            FROM turns
            WHERE thread_id = $thread
            ORDER BY id DESC
            LIMIT $limit;
            """,
            ReadTurn,
            ct,
            ("$thread", threadId),
            ("$limit", limit)).ConfigureAwait(false);

        // Read newest first so the limit keeps the newest, then handed back in the order they
        // were said.
        return rows.Reverse().ToList();
    }

    /// <summary>
    /// Finds older turns of this thread that mention what is being asked about now.
    /// </summary>
    /// <remarks>
    /// This is the alternative to a rolling digest. What falls out of the recent window is not
    /// compressed and it is not forgotten: it is on disk, indexed, and pulled back verbatim when
    /// the new message mentions it. Summarising a thread into a digest is what makes an assistant
    /// confidently claim not to know something that is sitting in its own transcript.
    /// </remarks>
    /// <param name="threadId">The conversation to look in.</param>
    /// <param name="query">The new message, used as the search.</param>
    /// <param name="excluding">Turn identities already carried verbatim, which must not be repeated.</param>
    /// <param name="limit">How many older turns are worth pulling back.</param>
    public async Task<IReadOnlyList<ConversationTurn>> RecallAsync(
        string threadId,
        string query,
        IReadOnlySet<string> excluding,
        int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<ConversationTurn>();
        }

        try
        {
            var rows = await ReadAsync(
                """
                SELECT t.turn_id, t.thread_id, t.role, t.text, t.at, t.run_id
                FROM search s
                JOIN turns t ON t.turn_id = s.event_id
                WHERE search MATCH $query AND t.thread_id = $thread
                ORDER BY rank
                LIMIT $limit;
                """,
                ReadTurn,
                ct,
                ("$query", Quote(query)),
                ("$thread", threadId),
                ("$limit", limit + excluding.Count)).ConfigureAwait(false);

            return rows.Where(t => !excluding.Contains(t.Id)).Take(limit).ToList();
        }
        catch (SqliteException)
        {
            // A search the matcher will not take is not a reason to fail a run. The recent
            // exchange is carried either way.
            return Array.Empty<ConversationTurn>();
        }
    }

    /// <summary>The thread currently being talked in, creating one on a project that has none.</summary>
    public async Task<string> ReadActiveThreadAsync(CancellationToken ct)
    {
        var rows = await ReadAsync(
            "SELECT value FROM meta WHERE key = 'active_thread';",
            reader => reader.GetString(0),
            ct).ConfigureAwait(false);

        if (rows.Count > 0 && !string.IsNullOrWhiteSpace(rows[0]))
        {
            return rows[0];
        }

        var thread = Guid.NewGuid().ToString();
        SetActiveThread(thread);
        return thread;
    }

    /// <summary>
    /// Starts a fresh conversation and returns it.
    /// </summary>
    /// <remarks>
    /// Nothing is deleted. The old thread keeps every turn and stays searchable, which is what
    /// makes starting over safe enough to do freely.
    /// </remarks>
    public string StartNewThread()
    {
        var thread = Guid.NewGuid().ToString();
        SetActiveThread(thread);
        return thread;
    }

    private void SetActiveThread(string threadId)
        => Enqueue(connection => Execute(
            connection,
            "INSERT INTO meta (key, value) VALUES ('active_thread', $value) "
            + "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
            ("$value", threadId)));

    private static ConversationTurn ReadTurn(SqliteDataReader reader)
        => new(
            reader.GetString(0),
            reader.GetString(1),
            Enum.TryParse<TurnRole>(reader.GetString(2), out var role) ? role : TurnRole.Graph,
            reader.GetString(3),
            DateTimeOffset.Parse(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5));
}
