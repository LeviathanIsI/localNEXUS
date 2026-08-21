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
    public Task<ChatCompletionResult> StreamChatAsync(
        ModelEndpoint endpoint,
        string systemPrompt,
        string userContent,
        double temperature,
        int maxTokens,
        IProgress<string>? onToken,
        CancellationToken ct)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(ChatMessage.System(systemPrompt));
        }

        messages.Add(ChatMessage.User(userContent));

        return StreamChatAsync(endpoint, messages, null, temperature, maxTokens, onToken, ct);
    }

    /// <inheritdoc />
    public async Task<ChatCompletionResult> StreamChatAsync(
        ModelEndpoint endpoint,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
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

        using var request = BuildRequest(endpoint, messages, tools, temperature, maxTokens);

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
        var toolCalls = new SortedDictionary<int, ToolCallBuilder>();
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

            ReadChunk(payload, accumulated, toolCalls, onToken, ref promptTokens, ref completionTokens, ref finishReason);
        }

        stopwatch.Stop();

        return new ChatCompletionResult(
            accumulated.ToString(),
            promptTokens,
            completionTokens,
            stopwatch.Elapsed,
            finishReason)
        {
            ToolCalls = toolCalls.Values
                .Where(b => !string.IsNullOrWhiteSpace(b.Name))
                .Select(b => new ToolCall(
                    string.IsNullOrEmpty(b.Id) ? b.Name : b.Id,
                    b.Name,
                    b.Arguments.Length == 0 ? "{}" : b.Arguments.ToString()))
                .ToList()
        };
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
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        double temperature,
        int maxTokens)
    {
        var body = BuildRequestBody(endpoint.ModelId, messages, tools, temperature, maxTokens);

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
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        double temperature,
        int maxTokens)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", modelId);

            writer.WriteStartArray("messages");

            foreach (var message in messages)
            {
                WriteMessage(writer, message);
            }

            writer.WriteEndArray();

            if (tools is { Count: > 0 })
            {
                writer.WriteStartArray("tools");

                foreach (var tool in tools)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function");
                    writer.WriteStartObject("function");
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);

                    // A tool with no schema still has to declare an object shape, because a
                    // server given a function with no parameters block will reject the request
                    // rather than assume one.
                    writer.WritePropertyName("parameters");

                    if (tool.ParametersSchema is { } schema)
                    {
                        schema.WriteTo(writer);
                    }
                    else
                    {
                        writer.WriteStartObject();
                        writer.WriteString("type", "object");
                        writer.WriteStartObject("properties");
                        writer.WriteEndObject();
                        writer.WriteEndObject();
                    }

                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteString("tool_choice", "auto");
            }

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

    private static void WriteMessage(Utf8JsonWriter writer, ChatMessage message)
    {
        writer.WriteStartObject();
        writer.WriteString("role", message.Role);

        // A tool result quotes the id of the call it answers. Without it the model cannot tell
        // which of several parallel calls came back.
        if (message.ToolCallId is { } toolCallId)
        {
            writer.WriteString("tool_call_id", toolCallId);
        }

        // An assistant turn that only called tools has no content, and the field still has to be
        // present and null rather than absent for some servers to accept the turn.
        if (message.Content is { } content)
        {
            writer.WriteString("content", content);
        }
        else
        {
            writer.WriteNull("content");
        }

        if (message.ToolCalls is { Count: > 0 } calls)
        {
            writer.WriteStartArray("tool_calls");

            foreach (var call in calls)
            {
                writer.WriteStartObject();
                writer.WriteString("id", call.Id);
                writer.WriteString("type", "function");
                writer.WriteStartObject("function");
                writer.WriteString("name", call.Name);
                writer.WriteString("arguments", call.ArgumentsJson);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Accumulates one tool call as it arrives across streamed chunks.
    /// </summary>
    /// <remarks>
    /// Tool calls stream the same way text does: the name arrives once and the arguments arrive
    /// as fragments that have to be concatenated in order. The index rather than the id is what
    /// identifies which call a fragment belongs to, because the id itself only appears on the
    /// first fragment.
    /// </remarks>
    private sealed class ToolCallBuilder
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public StringBuilder Arguments { get; } = new();
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
        SortedDictionary<int, ToolCallBuilder> toolCalls,
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

                    if (choice.TryGetProperty("delta", out var toolDelta)
                        && toolDelta.TryGetProperty("tool_calls", out var calls)
                        && calls.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var call in calls.EnumerateArray())
                        {
                            ReadToolCallDelta(call, toolCalls);
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

    /// <summary>
    /// Folds one streamed tool call fragment into the call it belongs to.
    /// </summary>
    private static void ReadToolCallDelta(JsonElement call, SortedDictionary<int, ToolCallBuilder> toolCalls)
    {
        // Servers that send a whole call in one frame omit the index, and zero is the right
        // answer for them because there is only one.
        var index = call.TryGetProperty("index", out var indexValue) && indexValue.TryGetInt32(out var parsed)
            ? parsed
            : 0;

        if (!toolCalls.TryGetValue(index, out var builder))
        {
            builder = new ToolCallBuilder();
            toolCalls[index] = builder;
        }

        if (call.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
        {
            builder.Id = id.GetString() ?? builder.Id;
        }

        if (!call.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (function.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            builder.Name = name.GetString() ?? builder.Name;
        }

        if (function.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.String)
        {
            builder.Arguments.Append(arguments.GetString());
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
