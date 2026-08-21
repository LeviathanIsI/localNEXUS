namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Which wire protocol an endpoint speaks.
/// </summary>
/// <remarks>
/// Three values because there are three request shapes in the world this application talks to,
/// not because there are three vendors. Most vendors chose the OpenAI shape, which is why one
/// client serves seven of them and adding an eighth costs a row of data.
/// </remarks>
public enum ModelWire
{
    /// <summary>The OpenAI chat completions shape, which most of the industry adopted.</summary>
    OpenAiCompatible,

    /// <summary>Anthropic's messages API, which has its own shape, header and stream events.</summary>
    Anthropic,

    /// <summary>Google's generative language API, which has its own again.</summary>
    Gemini
}

/// <summary>
/// One hosted provider: how to reach it, where to get a key, and what it charges.
/// </summary>
/// <param name="Id">Stable identifier, written into graphs and used as the credential key.</param>
/// <param name="DisplayName">What a person calls it.</param>
/// <param name="Wire">Which request shape it speaks.</param>
/// <param name="BaseUrl">Root of the API.</param>
/// <param name="KeyUrl">Where to go and get a key, so nobody has to hunt for the console.</param>
/// <param name="InputPerMillion">Dollars per million input tokens.</param>
/// <param name="OutputPerMillion">Dollars per million output tokens.</param>
/// <param name="SuggestedModels">Model ids offered as a starting point. The field stays free text.</param>
/// <remarks>
/// The rates are for the model most people will reach for at each provider, and they are a
/// starting point rather than a price list. A provider serves many models at many prices, and
/// this application cannot know which one somebody typed into a free text box.
/// </remarks>
public sealed record CloudProvider(
    string Id,
    string DisplayName,
    ModelWire Wire,
    string BaseUrl,
    string KeyUrl,
    decimal InputPerMillion,
    decimal OutputPerMillion,
    IReadOnlyList<string> SuggestedModels)
{
    /// <summary>True when this entry is one the user typed rather than one shipped here.</summary>
    public bool IsCustom => Id.StartsWith("custom.", StringComparison.OrdinalIgnoreCase);

    /// <summary>What the rates read as in the interface.</summary>
    public string RateSummary => InputPerMillion <= 0m && OutputPerMillion <= 0m
        ? "rates not known"
        : $"${InputPerMillion:0.##} in, ${OutputPerMillion:0.##} out per million tokens";
}
