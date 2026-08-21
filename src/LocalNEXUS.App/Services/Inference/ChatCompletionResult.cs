namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// The outcome of one streamed chat completion.
/// </summary>
/// <param name="Text">The full assistant message, reassembled from the streamed deltas.</param>
/// <param name="PromptTokens">Prompt token count, when the server reported usage.</param>
/// <param name="CompletionTokens">Completion token count, when the server reported usage.</param>
/// <param name="Elapsed">Wall clock time from request start to the end of the stream.</param>
/// <param name="FinishReason">Why generation stopped, when the server reported it.</param>
public sealed record ChatCompletionResult(
    string Text,
    int? PromptTokens,
    int? CompletionTokens,
    TimeSpan Elapsed,
    string? FinishReason)
{
    /// <summary>
    /// Tool calls the model asked for, empty unless tools were offered.
    /// </summary>
    /// <remarks>
    /// An init only property with a default rather than another positional parameter, so every
    /// place that already constructs one of these keeps compiling and keeps meaning the same
    /// thing.
    /// </remarks>
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = Array.Empty<ToolCall>();

    /// <summary>True when the model stopped in order to call something.</summary>
    public bool WantsTools => ToolCalls.Count > 0;

    /// <summary>A short summary suitable for the activity feed, for example "412 tokens in 6.2 s (66 tok/s)".</summary>
    public string Summary
    {
        get
        {
            var seconds = Elapsed.TotalSeconds;
            var parts = new List<string>();

            if (CompletionTokens is > 0)
            {
                parts.Add($"{CompletionTokens} tokens");

                if (seconds > 0.01d)
                {
                    parts.Add($"{CompletionTokens / seconds:0.0} tok/s");
                }
            }
            else
            {
                parts.Add($"{Text.Length} chars");
            }

            if (PromptTokens is > 0)
            {
                parts.Add($"{PromptTokens} prompt");
            }

            parts.Add($"{seconds:0.0} s");

            if (!string.IsNullOrWhiteSpace(FinishReason) && FinishReason != "stop")
            {
                parts.Add($"finish: {FinishReason}");
            }

            return string.Join(", ", parts);
        }
    }
}
