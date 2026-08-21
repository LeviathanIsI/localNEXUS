namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Sends a request to whichever client speaks the protocol at the far end.
/// </summary>
/// <remarks>
/// This is the whole of how three wire protocols became one request path. Everything upstream
/// still asks for a chat completion against an endpoint, exactly as it did when there was one
/// client, and the endpoint says which shape answers there.
///
/// Nodes do not know this exists. A node builds an endpoint and asks for a completion; which
/// adapter runs is decided here and nowhere else, which is what keeps adding a fourth protocol
/// to one file rather than to every caller.
/// </remarks>
public sealed class ModelClientRouter : IModelClient, IDisposable
{
    private readonly IModelClient _openAi;
    private readonly IModelClient _anthropic;
    private readonly IModelClient _gemini;

    private bool _disposed;

    public ModelClientRouter(IModelClient openAi, IModelClient anthropic, IModelClient gemini)
    {
        _openAi = openAi;
        _anthropic = anthropic;
        _gemini = gemini;
    }

    /// <inheritdoc />
    public Task<ChatCompletionResult> StreamChatAsync(
        ModelEndpoint endpoint,
        string systemPrompt,
        string userContent,
        double temperature,
        int maxTokens,
        IProgress<string>? onToken,
        CancellationToken ct)
        => For(endpoint).StreamChatAsync(endpoint, systemPrompt, userContent, temperature, maxTokens, onToken, ct);

    /// <inheritdoc />
    public Task<ChatCompletionResult> StreamChatAsync(
        ModelEndpoint endpoint,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        double temperature,
        int maxTokens,
        IProgress<string>? onToken,
        CancellationToken ct)
        => For(endpoint).StreamChatAsync(endpoint, messages, tools, temperature, maxTokens, onToken, ct);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        (_openAi as IDisposable)?.Dispose();
        (_anthropic as IDisposable)?.Dispose();
        (_gemini as IDisposable)?.Dispose();
    }

    private IModelClient For(ModelEndpoint endpoint) => endpoint.Wire switch
    {
        ModelWire.Anthropic => _anthropic,
        ModelWire.Gemini => _gemini,
        _ => _openAi
    };
}
