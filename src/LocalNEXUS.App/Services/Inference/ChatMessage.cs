using System.Text.Json.Nodes;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// One tool the model is told it may call.
/// </summary>
/// <param name="Name">The name the model uses to call it.</param>
/// <param name="Description">What it does. This is what the model reads to decide whether to call it.</param>
/// <param name="ParametersSchema">JSON schema for its arguments.</param>
/// <param name="ExtensionId">Which extension provides it, so a call can be routed back.</param>
/// <remarks>
/// The extension id is carried here rather than looked up later because tool names are not
/// unique across servers. Two Unity servers both offering <c>get_scene</c> is not a hypothetical,
/// and a call routed to the wrong one is a bug that would be very hard to see.
/// </remarks>
public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonObject? ParametersSchema,
    string ExtensionId);

/// <summary>
/// A tool call the model asked for.
/// </summary>
/// <param name="Id">The identifier the model gave it, which the result has to quote back.</param>
/// <param name="Name">Which tool.</param>
/// <param name="ArgumentsJson">Its arguments, as the JSON text the model produced.</param>
/// <remarks>
/// Arguments stay as text rather than being parsed here. A model sometimes emits arguments that
/// are not valid JSON, and the useful thing to do with that is hand it back and say so, which
/// needs the original text.
/// </remarks>
public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>
/// One message in a conversation.
/// </summary>
/// <param name="Role">system, user, assistant or tool.</param>
/// <param name="Content">The text, which is empty on an assistant turn that only called tools.</param>
/// <param name="ToolCalls">Calls this assistant turn asked for.</param>
/// <param name="ToolCallId">Which call this tool result answers. Set only on a tool message.</param>
/// <remarks>
/// A conversation rather than a system prompt and a user string is the whole reason the model
/// client changed. A tool loop is a sequence of turns, and the model has to see the calls it made
/// and the results it got, or it will make them again.
/// </remarks>
public sealed record ChatMessage(
    string Role,
    string? Content,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null)
{
    /// <summary>A system message.</summary>
    public static ChatMessage System(string content) => new("system", content);

    /// <summary>A user message.</summary>
    public static ChatMessage User(string content) => new("user", content);

    /// <summary>An assistant turn, which may carry text, tool calls, or both.</summary>
    public static ChatMessage Assistant(string? content, IReadOnlyList<ToolCall>? toolCalls = null)
        => new("assistant", content, toolCalls);

    /// <summary>The result of one tool call, answering the call with the id it was given.</summary>
    public static ChatMessage Tool(string toolCallId, string content)
        => new("tool", content, null, toolCallId);
}
