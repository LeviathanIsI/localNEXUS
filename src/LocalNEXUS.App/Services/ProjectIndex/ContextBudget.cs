namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>
/// How much of the project a request is allowed to carry, in characters.
/// </summary>
/// <remarks>
/// Characters rather than tokens, because the only tokenizer available here is the one inside
/// whichever server is serving the model, and four characters to a token is close enough for a
/// budget whose job is to stop a prompt overflowing rather than to fill it exactly.
///
/// The defaults assume the smallest window worth supporting, eight thousand tokens, and leave
/// room for the reply. Roughly a thousand tokens of project map, four thousand of candidate file
/// contents and a thousand of signatures emitted earlier in the same run. Everything is
/// progressive: the map is names only, contents are loaded for candidates alone, and what does
/// not fit is dropped in rank order rather than truncated mid file.
/// </remarks>
public sealed record ContextBudget
{
    /// <summary>Roughly how many characters make a token, for reporting the budget in both units.</summary>
    public const int CharactersPerToken = 4;

    /// <summary>The compact map of what the project already contains.</summary>
    public int MapCharacters { get; init; } = 4000;

    /// <summary>The full contents of the candidate files the request is about.</summary>
    public int CandidateCharacters { get; init; } = 16000;

    /// <summary>Signatures produced earlier in this same run, so later files can see them.</summary>
    public int EmittedSignatureCharacters { get; init; } = 4000;

    /// <summary>How many candidates ranking offers before contents are read at all.</summary>
    public int CandidateLimit { get; init; } = 12;

    /// <summary>Everything above, together.</summary>
    public int TotalCharacters => MapCharacters + CandidateCharacters + EmittedSignatureCharacters;

    /// <summary>The same total in approximate tokens, which is the number people think in.</summary>
    public int ApproximateTokens => TotalCharacters / CharactersPerToken;

    /// <summary>A sentence naming the budget, written to the feed so it is never a hidden number.</summary>
    public string Summary
        => $"Context budget {TotalCharacters} characters, roughly {ApproximateTokens} tokens: "
           + $"{MapCharacters} for the project map, {CandidateCharacters} for candidate files, "
           + $"{EmittedSignatureCharacters} for what this run has already written.";

    /// <summary>
    /// Trims text to a budget on a line boundary, saying how much was left out rather than
    /// cutting silently.
    /// </summary>
    public static string Fit(string text, int budget, string what)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= budget)
        {
            return text ?? string.Empty;
        }

        var cut = text.LastIndexOf('\n', Math.Min(budget, text.Length - 1));

        if (cut <= 0)
        {
            cut = budget;
        }

        var dropped = text.Length - cut;
        return text[..cut] + Environment.NewLine + $"... {dropped} more characters of {what} were left out to fit the context budget";
    }
}
