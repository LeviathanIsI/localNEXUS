using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Talks to any endpoint that implements the OpenAI chat completions API over server sent events.
/// </summary>
/// <remarks>
/// This is the single network path for both providers. A locally spawned llama-server and
/// OpenRouter accept the same request body and emit the same stream format, so the only
/// difference between them is the base URL and whether an authorization header is attached.
/// </remarks>
public sealed class OpenAiCompatibleClient : IModelClient, IDisposable
{
    private const string DataPrefix = "data:";
    private const string StreamTerminator = "[DONE]";

    /// <summary>Sent to OpenRouter so runs are attributable on their dashboard.</summary>
    private const string RefererHeaderValue = "https://github.com/LeviathanIsI/LocalNEXUS";

    /// <summary>Sent to OpenRouter as the application name.</summary>
    private const string TitleHeaderValue = "LocalNEXUS";

    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public OpenAiCompatibleClient()
        : this(CreateDefaultHttpClient(), ownsHttpClient: true)
    {
    }

    public OpenAiCompatibleClient(HttpClient http, bool ownsHttpClient = false)
    {
        _http = http;
        _ownsHttpClient = ownsHttpClient;
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult> StreamChatAsync(
        ModelEndpoint endpoint,
        string systemPrompt,
        string userContent,
        double temperature,
        int maxTokens,
        IProgress<string>? onToken,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl))
        {
            throw new ModelClientException("No base URL is configured for this node.");
        }

        if (string.IsNullOrWhiteSpace(endpoint.ModelId))
        {
            throw new ModelClientException("No model is selected for this node.");
        }

        using var request = BuildRequest(endpoint, systemPrompt, userContent, temperature, maxTokens);

        var stopwatch = Stopwatch.StartNew();

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBodySafelyAsync(response, ct).ConfigureAwait(false);
            throw new ModelClientException(
                $"{(int)response.StatusCode} {response.ReasonPhrase} from {endpoint.ChatCompletionsUrl}. {body}".TrimEnd());
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var accumulated = new StringBuilder();
        int? promptTokens = null;
        int? completionTokens = null;
        string? finishReason = null;

        // Read purely asynchronously. Testing EndOfStream would perform a blocking peek on a
        // stream that stays open between tokens, which would stall the calling thread.
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (!TryGetEventPayload(line, out var payload))
            {
                continue;
            }

            if (payload == StreamTerminator)
            {
                break;
            }

            ReadChunk(payload, accumulated, onToken, ref promptTokens, ref completionTokens, ref finishReason);
        }

        stopwatch.Stop();

        return new ChatCompletionResult(
            accumulated.ToString(),
            promptTokens,
            completionTokens,
            stopwatch.Elapsed,
            finishReason);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }

    /// <summary>
    /// Creates the shared client. The timeout is disabled because a long generation on a local
    /// model can easily outlast any fixed value; cancellation is driven by the run instead.
    /// </summary>
    public static HttpClient CreateDefaultHttpClient() => new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static HttpRequestMessage BuildRequest(
        ModelEndpoint endpoint,
        string systemPrompt,
        string userContent,
        double temperature,
        int maxTokens)
    {
        var body = BuildRequestBody(endpoint.ModelId, systemPrompt, userContent, temperature, maxTokens);

        var request = new HttpRequestMessage(HttpMethod.Post, endpoint.ChatCompletionsUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (endpoint.RequiresAuthorization)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.ApiKey);
            request.Headers.TryAddWithoutValidation("HTTP-Referer", RefererHeaderValue);
            request.Headers.TryAddWithoutValidation("X-Title", TitleHeaderValue);
        }

        return request;
    }

    private static string BuildRequestBody(
        string modelId,
        string systemPrompt,
        string userContent,
        double temperature,
        int maxTokens)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", modelId);

            writer.WriteStartArray("messages");

            if (!string.IsNullOrWhiteSpace(systemPrompt))
            {
                WriteMessage(writer, "system", systemPrompt);
            }

            WriteMessage(writer, "user", userContent);
            writer.WriteEndArray();

            writer.WriteNumber("temperature", temperature);
            writer.WriteNumber("max_tokens", maxTokens);
            writer.WriteBoolean("stream", true);

            // Both llama-server and OpenRouter report usage on the final chunk when asked.
            writer.WriteStartObject("stream_options");
            writer.WriteBoolean("include_usage", true);
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteMessage(Utf8JsonWriter writer, string role, string content)
    {
        writer.WriteStartObject();
        writer.WriteString("role", role);
        writer.WriteString("content", content);
        writer.WriteEndObject();
    }

    /// <summary>
    /// Extracts the payload of a server sent event line. Blank lines and comment lines that
    /// servers use as keepalives are skipped.
    /// </summary>
    private static bool TryGetEventPayload(string line, out string payload)
    {
        payload = string.Empty;

        if (string.IsNullOrWhiteSpace(line) || line.StartsWith(':'))
        {
            return false;
        }

        if (!line.StartsWith(DataPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        payload = line[DataPrefix.Length..].Trim();
        return payload.Length > 0;
    }

    private static void ReadChunk(
        string payload,
        StringBuilder accumulated,
        IProgress<string>? onToken,
        ref int? promptTokens,
        ref int? completionTokens,
        ref string? finishReason)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException)
        {
            // A partial or non JSON keepalive frame is not worth failing the whole run over.
            return;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.TryGetProperty("error", out var error))
            {
                throw new ModelClientException(ReadErrorMessage(error));
            }

            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.TryGetProperty("delta", out var delta)
                        && delta.TryGetProperty("content", out var content)
                        && content.ValueKind == JsonValueKind.String)
                    {
                        var text = content.GetString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            accumulated.Append(text);
                            onToken?.Report(text);
                        }
                    }

                    if (choice.TryGetProperty("finish_reason", out var reason)
                        && reason.ValueKind == JsonValueKind.String)
                    {
                        finishReason = reason.GetString();
                    }
                }
            }

            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                promptTokens = ReadTokenCount(usage, "prompt_tokens") ?? promptTokens;
                completionTokens = ReadTokenCount(usage, "completion_tokens") ?? completionTokens;
            }
        }
    }

    private static int? ReadTokenCount(JsonElement usage, string propertyName)
        => usage.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var count)
            ? count
            : null;

    private static string ReadErrorMessage(JsonElement error)
    {
        if (error.ValueKind == JsonValueKind.String)
        {
            return error.GetString() ?? "The endpoint returned an error.";
        }

        if (error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.String)
        {
            return message.GetString() ?? "The endpoint returned an error.";
        }

        return error.ToString();
    }

    private static async Task<string> ReadBodySafelyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return body.Length > 600 ? body[..600] : body;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            return string.Empty;
        }
    }
}
