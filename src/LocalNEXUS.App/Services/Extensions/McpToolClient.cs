using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Models.Extensions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Lists and calls the tools an MCP extension exposes.
/// </summary>
/// <remarks>
/// Thin on purpose. The official SDK does the protocol; this exists to turn its types into the
/// two shapes the rest of the application already speaks, which are a tool description a model
/// can be shown and a string a model can be given back.
/// <para>
/// A tool that fails returns its failure as text rather than throwing. That is not sloppiness:
/// a model handed "that path does not exist" will try a different path, and a model handed an
/// exception that faulted the run cannot do anything at all. It is the same reasoning as the
/// compile repair loop, where the diagnostics go back to the model rather than stopping
/// everything.
/// </para>
/// </remarks>
public sealed class McpToolClient
{
    private readonly McpClient _client;

    public McpToolClient(ExtensionSession session)
        => _client = session.Mcp
            ?? throw new ExtensionException(
                "This session is not an MCP session, so tools cannot be listed over it.");

    /// <summary>Asks the server what tools it has. The server is the authority, not the manifest.</summary>
    public async Task<IReadOnlyList<ToolContribution>> ListToolsAsync(CancellationToken ct)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);

        return tools
            .Select(tool => new ToolContribution(
                tool.Name,
                tool.Description ?? string.Empty,
                SchemaOf(tool)))
            .ToList();
    }

    /// <summary>
    /// Calls one tool and returns what it said, as text a model can read.
    /// </summary>
    /// <returns>The result text, and whether the tool reported it as a failure.</returns>
    public async Task<(string Text, bool IsError)> CallAsync(
        string toolName,
        JsonObject arguments,
        CancellationToken ct)
    {
        var typed = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var pair in arguments)
        {
            typed[pair.Key] = pair.Value?.ToJsonString() is { } json
                ? JsonSerializer.Deserialize<JsonElement>(json)
                : null;
        }

        try
        {
            var result = await _client.CallToolAsync(toolName, typed, cancellationToken: ct).ConfigureAwait(false);
            return (Flatten(result), result.IsError ?? false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Back to the model as a result rather than up as a fault, so it can correct itself.
            return ($"The tool '{toolName}' could not be called: {ex.Message}", true);
        }
    }

    private static JsonObject? SchemaOf(McpClientTool tool)
    {
        try
        {
            return JsonNode.Parse(tool.JsonSchema.GetRawText()) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Flatten(CallToolResult result)
    {
        var text = new StringBuilder();

        foreach (var block in result.Content)
        {
            if (block is TextContentBlock textBlock)
            {
                text.AppendLine(textBlock.Text);
                continue;
            }

            // Anything that is not text is named rather than dropped, so a model that asked for
            // a screenshot is told it got one instead of silently getting nothing.
            text.AppendLine($"[{block.Type} content]");
        }

        if (text.Length == 0 && result.StructuredContent is { } structured)
        {
            text.Append(structured.GetRawText());
        }

        return text.Length == 0 ? "The tool returned nothing." : text.ToString().TrimEnd();
    }
}
