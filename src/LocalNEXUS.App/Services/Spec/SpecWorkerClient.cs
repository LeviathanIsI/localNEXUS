using System.Text.Json.Nodes;
using LocalNEXUS.App.Services.Extensions;

namespace LocalNEXUS.App.Services.Spec;

/// <summary>
/// Speaks the spec contract to a worker process.
/// </summary>
/// <remarks>
/// The wire protocol, stated once here because it is the thing the extension author has to
/// implement:
/// <list type="bullet">
/// <item><c>spec/describe</c>, host to worker, no parameters. Returns what it is bridging to and
/// which folder it found, so the tab can say what it is looking at rather than showing an empty
/// list that could mean either nothing to do or nothing found.</item>
/// <item><c>spec/changes</c>, host to worker, no parameters. Returns every change with its
/// artifacts and the state of each, done, ready or blocked.</item>
/// <item><c>spec/artifact</c>, host to worker, with a change id and an artifact id. Returns the
/// text of that artifact and where it lives.</item>
/// <item><c>spec/advance</c>, host to worker, with a change id. Asks the tool to create the next
/// artifact that is ready, and returns what to say plus the change as it now stands.</item>
/// <item><c>spec/log</c>, worker to host, a notification that reaches the activity feed.</item>
/// </list>
/// Newline delimited JSON-RPC 2.0 over stdio, which is the framing the node contract and MCP both
/// use. A third framing for a third contract would have bought nothing and cost an extension author
/// a protocol nobody else speaks.
///
/// Every one of these is a question. Nothing here computes which artifact is next, whether a change
/// is complete, or what a delta merges to, because all of that is what OpenSpec is and a second
/// implementation would drift from it.
/// </remarks>
public sealed class SpecWorkerClient
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long advancing may take.
    /// </summary>
    /// <remarks>
    /// Longer than a read, because creating the next artifact is the tool doing work rather than
    /// answering from what it already has.
    /// </remarks>
    private static readonly TimeSpan AdvanceTimeout = TimeSpan.FromMinutes(10);

    private readonly JsonRpcConnection _connection;

    public SpecWorkerClient(ExtensionSession session)
        => _connection = session.Rpc
            ?? throw new ExtensionException(
                "This session is not a spec contract session, so the spec protocol cannot be spoken over it.");

    /// <summary>Asks the worker what it is bridging to.</summary>
    public async Task<SpecWorkerInfo> DescribeAsync(CancellationToken ct)
    {
        var payload = await ObjectAsync("spec/describe", null, ReadTimeout, ct).ConfigureAwait(false);

        return new SpecWorkerInfo(
            Text(payload, "tool") ?? "OpenSpec",
            Text(payload, "version") ?? "unknown",
            Text(payload, "root"));
    }

    /// <summary>Every change the tool knows about, with its artifacts.</summary>
    public async Task<IReadOnlyList<SpecChange>> ListChangesAsync(CancellationToken ct)
    {
        var payload = await ObjectAsync("spec/changes", null, ReadTimeout, ct).ConfigureAwait(false);

        if (payload["changes"] is not JsonArray changes)
        {
            throw new ExtensionException(
                "The worker answered spec/changes without a 'changes' array, so nothing could be read.");
        }

        return changes.OfType<JsonObject>().Select(ReadChange).ToList();
    }

    /// <summary>The text of one artifact.</summary>
    public async Task<SpecArtifactContent> ReadArtifactAsync(string changeId, string artifactId, CancellationToken ct)
    {
        var parameters = new JsonObject
        {
            ["changeId"] = changeId,
            ["artifactId"] = artifactId
        };

        var payload = await ObjectAsync("spec/artifact", parameters, ReadTimeout, ct).ConfigureAwait(false);

        return new SpecArtifactContent(Text(payload, "content") ?? string.Empty, Text(payload, "path"));
    }

    /// <summary>
    /// Asks the tool to create the next artifact that is ready.
    /// </summary>
    /// <returns>What to report, and the change as it stands afterwards.</returns>
    public async Task<(string Message, SpecChange? Change)> AdvanceAsync(string changeId, CancellationToken ct)
    {
        var parameters = new JsonObject { ["changeId"] = changeId };

        var payload = await ObjectAsync("spec/advance", parameters, AdvanceTimeout, ct).ConfigureAwait(false);

        return (
            Text(payload, "message") ?? "The change was advanced.",
            payload["change"] is JsonObject change ? ReadChange(change) : null);
    }

    private async Task<JsonObject> ObjectAsync(string method, JsonObject? parameters, TimeSpan timeout, CancellationToken ct)
    {
        var result = await _connection.InvokeAsync(method, parameters, timeout, ct).ConfigureAwait(false);

        return result as JsonObject
               ?? throw new ExtensionException($"The worker answered {method} with something that was not an object.");
    }

    private static SpecChange ReadChange(JsonObject entry) => SpecWorkerReader.ReadChange(entry);

    private static string? Text(JsonObject payload, string name) => SpecWorkerReader.Text(payload, name);
}
