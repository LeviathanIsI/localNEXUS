using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// A model client that answers from a script instead of from a model.
/// </summary>
/// <remarks>
/// This is what makes the plumbing testable at all. A repair loop, a tool loop, a debate round and
/// an elicitation are all sequences of calls with decisions between them, and none of those
/// decisions can be tested against something that answers differently every time.
///
/// It drops in because <see cref="IModelClient"/> is already an interface and already the only way
/// anything reaches a model. Nothing in the application was changed to allow this.
///
/// Replies are handed out in order and the calls are recorded, so a test can assert both on what
/// came out and on what was asked, which is where a repair loop actually goes wrong: it is easy to
/// loop the right number of times and never send the errors.
/// </remarks>
public sealed class StubModelClient : IModelClient
{
    private readonly Queue<string> _replies = new();
    private readonly List<Recorded> _calls = new();

    private string _whenEmpty = "no reply was scripted";

    /// <summary>One call this client was asked to make.</summary>
    /// <param name="SystemPrompt">The system message, or the first system message of a conversation.</param>
    /// <param name="UserContent">Everything the model was shown, joined.</param>
    /// <param name="MaxTokens">The ceiling the caller asked for.</param>
    public sealed record Recorded(string SystemPrompt, string UserContent, int MaxTokens);

    /// <summary>Every call, in order.</summary>
    public IReadOnlyList<Recorded> Calls => _calls;

    /// <summary>How many times anything asked this for a completion.</summary>
    public int CallCount => _calls.Count;

    /// <summary>Queues the next reply.</summary>
    public StubModelClient Reply(string text)
    {
        _replies.Enqueue(text);
        return this;
    }

    /// <summary>Queues several replies, in order.</summary>
    public StubModelClient Replies(params string[] texts)
    {
        foreach (var text in texts)
        {
            _replies.Enqueue(text);
        }

        return this;
    }

    /// <summary>What to answer once the script has run out. Defaults to something recognisable.</summary>
    public StubModelClient ThenAlways(string text)
    {
        _whenEmpty = text;
        return this;
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
        _calls.Add(new Recorded(systemPrompt, userContent, maxTokens));
        return Task.FromResult(Next(onToken));
    }

    /// <inheritdoc />
    public Task<ChatCompletionResult> StreamChatAsync(
        ModelEndpoint endpoint,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition>? tools,
        double temperature,
        int maxTokens,
        IProgress<string>? onToken,
        CancellationToken ct)
    {
        var system = messages.FirstOrDefault(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase));
        var rest = messages.Where(m => m != system).Select(m => m.Content ?? string.Empty);

        _calls.Add(new Recorded(system?.Content ?? string.Empty, string.Join(Environment.NewLine, rest), maxTokens));
        return Task.FromResult(Next(onToken));
    }

    private ChatCompletionResult Next(IProgress<string>? onToken)
    {
        var text = _replies.Count > 0 ? _replies.Dequeue() : _whenEmpty;

        // Streamed in one chunk. Anything reading the stream is being tested on what it does with
        // the text, not on how many pieces it arrived in.
        onToken?.Report(text);

        return new ChatCompletionResult(text, 10, 20, TimeSpan.FromMilliseconds(1), "stop");
    }
}
