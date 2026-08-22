using System.Diagnostics;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.Evals;

/// <summary>
/// The real client, with every call weighed on the way through.
/// </summary>
/// <remarks>
/// A decorator rather than anything inside the application, and it is the reason no production
/// change was needed to measure tokens: <see cref="IModelClient"/> is already the only way
/// anything reaches a model, so wrapping it sees every call the run makes and changes none of
/// them.
///
/// Time to first token is worth having separately from total time. A model that takes twenty
/// seconds to start answering and then streams quickly feels entirely different from one that
/// starts immediately and grinds, and a single duration cannot tell them apart.
/// </remarks>
public sealed class MeasuringModelClient : IModelClient
{
    private readonly IModelClient _inner;
    private readonly List<Call> _calls = new();
    private readonly object _sync = new();

    /// <summary>How much of a reply is kept, which is enough to read and not enough to bloat.</summary>
    private const int ReplyKept = 4000;

    public MeasuringModelClient(IModelClient inner) => _inner = inner;

    /// <summary>One request and what it cost.</summary>
    /// <param name="PromptTokens">As reported by the server, or null when it reported none.</param>
    /// <param name="CompletionTokens">As reported by the server, or null when it reported none.</param>
    /// <param name="Elapsed">Wall time for the whole request.</param>
    /// <param name="ToFirstToken">Wall time until the first streamed chunk, or null when nothing streamed.</param>
    /// <param name="Characters">How long the reply was, which is measurable even when tokens are not.</param>
    /// <param name="FinishReason">Why the model stopped. A length stop is a truncated answer.</param>
    /// <param name="Reply">
    /// What came back, truncated. Kept because a task that fails the same way every run fails for
    /// a reason that is in the reply, and nothing else keeps one: the nodes parse a reply, act on
    /// it and discard it, so a run that quietly did nothing leaves no trace of what it was told.
    /// </param>
    public sealed record Call(
        int? PromptTokens,
        int? CompletionTokens,
        TimeSpan Elapsed,
        TimeSpan? ToFirstToken,
        int Characters,
        string? FinishReason,
        string Reply);

    /// <summary>Every call made since the last reset, in order.</summary>
    public IReadOnlyList<Call> Calls
    {
        get
        {
            lock (_sync)
            {
                return _calls.ToList();
            }
        }
    }

    /// <summary>Forgets everything, so one task's numbers are only that task's.</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _calls.Clear();
        }
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
        var watch = Stopwatch.StartNew();
        var first = new FirstTokenClock(watch, onToken);

        var result = await _inner
            .StreamChatAsync(endpoint, systemPrompt, userContent, temperature, maxTokens, first, ct)
            .ConfigureAwait(false);

        Record(result, watch, first);
        return result;
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
        var watch = Stopwatch.StartNew();
        var first = new FirstTokenClock(watch, onToken);

        var result = await _inner
            .StreamChatAsync(endpoint, messages, tools, temperature, maxTokens, first, ct)
            .ConfigureAwait(false);

        Record(result, watch, first);
        return result;
    }

    private void Record(ChatCompletionResult result, Stopwatch watch, FirstTokenClock first)
    {
        watch.Stop();

        lock (_sync)
        {
            var reply = result.Text ?? string.Empty;

            _calls.Add(new Call(
                result.PromptTokens,
                result.CompletionTokens,
                watch.Elapsed,
                first.Elapsed,
                reply.Length,
                result.FinishReason,
                reply.Length <= ReplyKept ? reply : reply[..ReplyKept] + " ... (truncated)"));
        }
    }

    /// <summary>Notes when the first chunk arrived and passes everything on unchanged.</summary>
    private sealed class FirstTokenClock : IProgress<string>
    {
        private readonly Stopwatch _watch;
        private readonly IProgress<string>? _inner;

        public FirstTokenClock(Stopwatch watch, IProgress<string>? inner)
        {
            _watch = watch;
            _inner = inner;
        }

        public TimeSpan? Elapsed { get; private set; }

        public void Report(string value)
        {
            Elapsed ??= _watch.Elapsed;
            _inner?.Report(value);
        }
    }
}
