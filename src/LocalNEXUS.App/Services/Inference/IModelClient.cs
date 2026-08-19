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
}
