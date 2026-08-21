namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Sends a chat request to an OpenAI compatible endpoint and streams the reply back.
/// </summary>
public interface IModelClient
{
    /// <summary>
    /// Streams one chat completion.
    /// </summary>
    /// <param name="endpoint">Where to send the request and which model to ask for.</param>
    /// <param name="systemPrompt">System message. Omitted from the request when blank.</param>
    /// <param name="userContent">User message.</param>
    /// <param name="temperature">Sampling temperature.</param>
    /// <param name="maxTokens">Upper bound on generated tokens.</param>
    /// <param name="onToken">Receives each streamed chunk as it arrives.</param>
    /// <param name="ct">Cancels the request and the stream.</param>
    /// <returns>The complete reply along with usage and timing.</returns>
    /// <exception cref="ModelClientException">The endpoint rejected the request or returned a malformed stream.</exception>
    Task<ChatCompletionResult> StreamChatAsync(
        ModelEndpoint endpoint,
        string systemPrompt,
        string userContent,
        double temperature,
        int maxTokens,
        IProgress<string>? onToken,
        CancellationToken ct);

    /// <summary>
    /// Streams one chat completion over a whole conversation, optionally offering tools.
    /// </summary>
    /// <param name="endpoint">Where to send the request and which model to ask for.</param>
    /// <param name="messages">The conversation so far, in order.</param>
    /// <param name="tools">Tools the model may call, or null to offer none.</param>
    /// <param name="temperature">Sampling temperature.</param>
    /// <param name="maxTokens">Upper bound on generated tokens.</param>
    /// <param name="onToken">Receives each streamed chunk as it arrives.</param>
    /// <param name="ct">Cancels the request and the stream.</param>
    /// <returns>The reply, its usage and timing, and any tool calls the model asked for.</returns>
    /// <remarks>
    /// Added rather than replacing the single prompt overload, which is now a thin wrapper over
    /// this one. A tool loop is a sequence of turns and cannot be expressed as a system prompt
    /// and a user string, but almost every node in the application does exactly one turn and
    /// should not have to build a list to say so.
    /// <para>
    /// Passing null for <paramref name="tools"/> produces exactly the request this client sent
    /// before any of this existed, so nothing about the ordinary path changed.
    /// </para>
    /// </remarks>
    /// <exception cref="ModelClientException">The endpoint rejected the request or returned a malformed stream.</exception>
    Task<ChatCompletionResult> StreamChatAsync(
        ModelEndpoint endpoint,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        double temperature,
        int maxTokens,
        IProgress<string>? onToken,
        CancellationToken ct);
}
