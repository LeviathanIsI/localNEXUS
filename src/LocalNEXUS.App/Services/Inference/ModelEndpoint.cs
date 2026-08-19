namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Everything needed to reach one OpenAI compatible chat endpoint.
/// </summary>
/// <remarks>
/// A locally spawned llama-server and OpenRouter differ only in these three values, which is
/// why a single client implementation serves both providers.
/// </remarks>
/// <param name="BaseUrl">Root of the API, without a trailing path, for example <c>https://openrouter.ai/api/v1</c>.</param>
/// <param name="ModelId">Model identifier sent in the request body.</param>
/// <param name="ApiKey">Bearer token, or null for an endpoint that needs no authentication.</param>
public sealed record ModelEndpoint(string BaseUrl, string ModelId, string? ApiKey = null)
{
    /// <summary>The chat completions URL for this endpoint.</summary>
    public string ChatCompletionsUrl => $"{BaseUrl.TrimEnd('/')}/chat/completions";

    /// <summary>True when requests to this endpoint must carry an authorization header.</summary>
    public bool RequiresAuthorization => !string.IsNullOrWhiteSpace(ApiKey);
}
