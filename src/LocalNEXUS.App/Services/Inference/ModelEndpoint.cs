namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Everything needed to reach one chat endpoint.
/// </summary>
/// <remarks>
/// A locally spawned llama-server and OpenRouter differ only in the url, the model and the key,
/// which is why one client served both for as long as everything spoke the same shape.
///
/// Two providers do not, so the record carries which wire protocol answers at this address. That
/// is the whole of the change: the router reads it and picks a client, and nothing that builds an
/// endpoint has to know a client exists. Anything not saying otherwise is OpenAI compatible,
/// which keeps every existing call site correct without touching it.
/// </remarks>
/// <param name="BaseUrl">Root of the API, without a trailing path, for example <c>https://openrouter.ai/api/v1</c>.</param>
/// <param name="ModelId">Model identifier sent in the request.</param>
/// <param name="ApiKey">The key, or null for an endpoint that needs no authentication.</param>
/// <param name="Wire">Which request shape answers here.</param>
/// <param name="ProviderId">Which catalogue entry this came from, for pricing. Empty for a local model.</param>
public sealed record ModelEndpoint(
    string BaseUrl,
    string ModelId,
    string? ApiKey = null,
    ModelWire Wire = ModelWire.OpenAiCompatible,
    string ProviderId = "")
{
    /// <summary>The chat completions URL for an OpenAI compatible endpoint.</summary>
    public string ChatCompletionsUrl => $"{BaseUrl.TrimEnd('/')}/chat/completions";

    /// <summary>The messages URL for an Anthropic endpoint.</summary>
    public string MessagesUrl => $"{BaseUrl.TrimEnd('/')}/messages";

    /// <summary>True when requests to this endpoint must carry an authorization header.</summary>
    public bool RequiresAuthorization => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>True when this is a hosted provider rather than something served on this machine.</summary>
    public bool IsCloud => Wire is not ModelWire.OpenAiCompatible || !string.IsNullOrWhiteSpace(ProviderId);

    /// <summary>
    /// The streaming URL for a Gemini endpoint, key included, because Gemini takes the key as a
    /// query parameter rather than a header.
    /// </summary>
    /// <remarks>
    /// Never log, display or write this. Use <see cref="SafeUrlFor"/> for anything a person or a
    /// file will see. A key in a query string is a key in every log line that quotes the url,
    /// which would put back exactly what the credential store exists to remove.
    /// </remarks>
    public string GeminiStreamUrl =>
        $"{BaseUrl.TrimEnd('/')}/models/{ModelId}:streamGenerateContent?alt=sse&key={ApiKey}";

    /// <summary>
    /// A url safe to show, with any key replaced.
    /// </summary>
    /// <remarks>
    /// Applied to every url that reaches a message, a log or the feed. It is written as a
    /// replacement of the key rather than a rebuild of the url so that it cannot miss a shape
    /// somebody adds later: whatever the url is, if the key is in it, it comes out.
    /// </remarks>
    public string SafeUrlFor(string url)
        => string.IsNullOrWhiteSpace(ApiKey) ? url : url.Replace(ApiKey, "[key removed]", StringComparison.Ordinal);
}
