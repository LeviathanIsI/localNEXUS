using System.Text.Json.Nodes;

namespace LocalNEXUS.App.Services.Spec;

/// <summary>
/// Turns what a worker said into the shapes the tab binds to.
/// </summary>
/// <remarks>
/// Separate from the client so it can be held to account without a process. What a worker sends is
/// the one part of this that another program decides, so the interesting cases, a state this build
/// has never heard of, a change with no name, an artifact list that is missing, are all things to
/// pin rather than to hope about.
///
/// It reads and does not reason. Nothing here works out which artifact is next or whether a change
/// is finished, because those are what OpenSpec is for.
/// </remarks>
public static class SpecWorkerReader
{
    /// <summary>One change, as the worker described it.</summary>
    public static SpecChange ReadChange(JsonObject entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var artifacts = new List<SpecArtifact>();

        if (entry["artifacts"] is JsonArray list)
        {
            foreach (var artifact in list.OfType<JsonObject>())
            {
                var id = Text(artifact, "id") ?? string.Empty;

                artifacts.Add(new SpecArtifact(
                    id,
                    Text(artifact, "name") ?? (id.Length > 0 ? id : "unnamed"),
                    ReadState(Text(artifact, "state")),
                    Text(artifact, "detail")));
            }
        }

        var changeId = Text(entry, "id") ?? string.Empty;

        return new SpecChange(
            changeId,
            Text(entry, "name") ?? changeId,
            string.Equals(Text(entry, "status"), "archived", StringComparison.OrdinalIgnoreCase)
                ? SpecChangeStatus.Archived
                : SpecChangeStatus.Active,
            artifacts);
    }

    /// <summary>
    /// The state, or Unknown when the worker said something this build has not heard of.
    /// </summary>
    /// <remarks>
    /// Unknown rather than a guess. A worker newer than this application may report a state that
    /// did not exist when this was written, and mapping it onto the nearest one would be inventing
    /// a claim about somebody's change.
    /// </remarks>
    public static SpecArtifactState ReadState(string? state) => state?.ToLowerInvariant() switch
    {
        "done" or "complete" or "completed" => SpecArtifactState.Done,
        "ready" => SpecArtifactState.Ready,
        "blocked" => SpecArtifactState.Blocked,
        _ => SpecArtifactState.Unknown
    };

    /// <summary>One property as text, or null when it is absent or is not text.</summary>
    public static string? Text(JsonObject payload, string name)
        => payload[name]?.GetValueKind() == System.Text.Json.JsonValueKind.String
            ? payload[name]!.GetValue<string>()
            : null;
}
