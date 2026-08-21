using System.Net.Http;
using System.Text.Json;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>What is known about a model's ability to call tools.</summary>
public enum ToolSupport
{
    /// <summary>The server says it supports tools.</summary>
    Supported,

    /// <summary>The server says its chat template has no tool support, so offering tools is pointless.</summary>
    Unsupported,

    /// <summary>Nothing could be established, which is the ordinary answer for a hosted endpoint.</summary>
    Unknown
}

/// <summary>
/// Asks a local model server whether the model it is serving can call tools.
/// </summary>
/// <remarks>
/// Worth doing because the failure mode is otherwise indistinguishable from a bug in this
/// application. A model with no tool template does not refuse: it ignores the tools and answers
/// in prose, or emits something that looks like a function call as ordinary text. Somebody
/// watching that happen has no way to tell that the model was never able to do what was asked.
/// <para>
/// llama.cpp answers this directly. Its <c>/props</c> endpoint returns a
/// <c>chat_template_caps</c> object with a <c>supports_tools</c> flag, which is read here rather
/// than guessed at by looking for the word "tools" in the template text.
/// </para>
/// <para>
/// Measured on llama.cpp b10488 rather than taken from documentation: the <c>--jinja</c> flag
/// that older guidance says is required for tool calling is not required on that build. Tools
/// work without it and the reported capabilities are identical either way, which is why nothing
/// was added to how the server is launched.
/// </para>
/// <para>
/// A hosted endpoint has no such endpoint to ask, so the answer there is Unknown and the request
/// is attempted. That is the honest result: guessing that OpenRouter supports tools would be
/// right most of the time, and wrong silently the rest.
/// </para>
/// </remarks>
public sealed class ToolSupportProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;

    public ToolSupportProbe(HttpClient http) => _http = http;

    /// <summary>
    /// Asks the server behind this endpoint whether its model can call tools.
    /// </summary>
    /// <returns>The answer, and a sentence explaining it for the feed.</returns>
    public async Task<(ToolSupport Support, string Detail)> ProbeAsync(ModelEndpoint endpoint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint.BaseUrl))
        {
            return (ToolSupport.Unknown, "No base url, so nothing could be asked.");
        }

        var url = $"{endpoint.BaseUrl.TrimEnd('/')}/props";

        try
        {
            using var timer = new CancellationTokenSource(Timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, ct);

            using var response = await _http.GetAsync(url, linked.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return (ToolSupport.Unknown, "This server does not report its capabilities.");
            }

            var body = await response.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);

            if (!document.RootElement.TryGetProperty("chat_template_caps", out var caps)
                || caps.ValueKind != JsonValueKind.Object)
            {
                return (ToolSupport.Unknown, "This server does not report chat template capabilities.");
            }

            var supportsTools = caps.TryGetProperty("supports_tools", out var tools)
                && tools.ValueKind == JsonValueKind.True;

            var supportsToolCalls = caps.TryGetProperty("supports_tool_calls", out var toolCalls)
                && toolCalls.ValueKind == JsonValueKind.True;

            if (supportsTools && supportsToolCalls)
            {
                var parallel = caps.TryGetProperty("supports_parallel_tool_calls", out var many)
                    && many.ValueKind == JsonValueKind.True;

                return (ToolSupport.Supported,
                    parallel
                        ? "This model can call tools, including several at once."
                        : "This model can call tools, one at a time.");
            }

            return (ToolSupport.Unsupported,
                "This model's chat template has no tool support, so it cannot call tools. " +
                "It will ignore them and answer in prose. Pick a model with a tool calling template.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            return (ToolSupport.Unknown, "The server did not answer, so tool support is unknown.");
        }
    }
}
