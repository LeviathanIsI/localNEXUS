using System.Text.Json.Nodes;

namespace LocalNEXUS.App.Models.Extensions;

/// <summary>
/// One tool an MCP extension exposes to a model.
/// </summary>
/// <param name="Name">Tool name as the model calls it.</param>
/// <param name="Description">What it does. This is what the model reads to decide whether to call it.</param>
/// <param name="InputSchema">JSON schema for its arguments.</param>
/// <remarks>
/// Read from the server rather than from the manifest wherever possible. A manifest is written by
/// hand and drifts; the running server is the authority on what it actually has. The manifest's
/// copy exists so the details pane can say something useful before the extension has ever been
/// started.
/// </remarks>
public sealed record ToolContribution(string Name, string Description, JsonObject? InputSchema = null);
