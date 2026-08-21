namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// The hosted providers this application knows how to reach.
/// </summary>
/// <remarks>
/// This file is the price list and the address book, and it is the only place either lives.
/// Rates change, providers rename models, and new ones appear; correcting any of that should be
/// editing a row here and nothing else.
///
/// Rates are dollars per million tokens, taken from each provider's public pricing page, and are
/// for the model most people reach for at that provider rather than for every model it serves.
/// The model id is a free text field precisely because a provider serves many models at many
/// prices, so a figure shown in the interface is an estimate at the listed rate and not a quote.
///
/// Seven of the nine speak the OpenAI shape and cost nothing to add beyond a row. Two do not, and
/// those two are why there are adapters.
/// </remarks>
public static class ProviderCatalog
{
    /// <summary>The identifier a node stores when it has not been pointed at a provider yet.</summary>
    public const string None = "";

    /// <summary>Every provider shipped with this build, in the order the interface lists them.</summary>
    public static IReadOnlyList<CloudProvider> All { get; } = new[]
    {
        new CloudProvider(
            "openai",
            "OpenAI",
            ModelWire.OpenAiCompatible,
            "https://api.openai.com/v1",
            "https://platform.openai.com/api-keys",
            2.50m,
            10.00m,
            new[] { "gpt-5", "gpt-5-mini", "gpt-4.1", "gpt-4o", "o4-mini" }),

        new CloudProvider(
            "anthropic",
            "Anthropic",
            ModelWire.Anthropic,
            "https://api.anthropic.com/v1",
            "https://console.anthropic.com/settings/keys",
            3.00m,
            15.00m,
            new[] { "claude-opus-5", "claude-sonnet-5", "claude-haiku-4-5-20251001" }),

        new CloudProvider(
            "gemini",
            "Google Gemini",
            ModelWire.Gemini,
            "https://generativelanguage.googleapis.com/v1beta",
            "https://aistudio.google.com/apikey",
            1.25m,
            10.00m,
            new[] { "gemini-2.5-pro", "gemini-2.5-flash", "gemini-2.0-flash" }),

        new CloudProvider(
            "openrouter",
            "OpenRouter",
            ModelWire.OpenAiCompatible,
            "https://openrouter.ai/api/v1",
            "https://openrouter.ai/keys",
            0m,
            0m,
            new[] { "anthropic/claude-sonnet-4.5", "openai/gpt-5", "deepseek/deepseek-chat" }),

        new CloudProvider(
            "deepseek",
            "DeepSeek",
            ModelWire.OpenAiCompatible,
            "https://api.deepseek.com/v1",
            "https://platform.deepseek.com/api_keys",
            0.28m,
            0.42m,
            new[] { "deepseek-chat", "deepseek-reasoner" }),

        new CloudProvider(
            "groq",
            "Groq",
            ModelWire.OpenAiCompatible,
            "https://api.groq.com/openai/v1",
            "https://console.groq.com/keys",
            0.59m,
            0.79m,
            new[] { "llama-3.3-70b-versatile", "qwen-2.5-coder-32b" }),

        new CloudProvider(
            "mistral",
            "Mistral",
            ModelWire.OpenAiCompatible,
            "https://api.mistral.ai/v1",
            "https://console.mistral.ai/api-keys",
            2.00m,
            6.00m,
            new[] { "mistral-large-latest", "codestral-latest" }),

        new CloudProvider(
            "together",
            "Together",
            ModelWire.OpenAiCompatible,
            "https://api.together.xyz/v1",
            "https://api.together.ai/settings/api-keys",
            0.88m,
            0.88m,
            new[] { "Qwen/Qwen2.5-Coder-32B-Instruct", "meta-llama/Llama-3.3-70B-Instruct-Turbo" }),

        new CloudProvider(
            "fireworks",
            "Fireworks",
            ModelWire.OpenAiCompatible,
            "https://api.fireworks.ai/inference/v1",
            "https://fireworks.ai/account/api-keys",
            0.90m,
            0.90m,
            new[] { "accounts/fireworks/models/qwen2p5-coder-32b-instruct" }),

        new CloudProvider(
            "xai",
            "xAI",
            ModelWire.OpenAiCompatible,
            "https://api.x.ai/v1",
            "https://console.x.ai",
            3.00m,
            15.00m,
            new[] { "grok-4", "grok-3-mini" })
    };

    /// <summary>Finds a provider by id, or null when nothing shipped or saved carries that id.</summary>
    public static CloudProvider? Find(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds a provider for an OpenAI compatible endpoint somebody typed in themselves.
    /// </summary>
    /// <remarks>
    /// The escape hatch, and the reason this list is not a treadmill. A provider nobody here
    /// anticipated works without a code change, as long as it speaks the shape most of them do.
    /// Rates are left at zero because there is no way to know them, and the cost display says
    /// so rather than showing a confident zero.
    /// </remarks>
    public static CloudProvider Custom(string name, string baseUrl) => new(
        $"custom.{Slug(name)}",
        string.IsNullOrWhiteSpace(name) ? "Custom endpoint" : name.Trim(),
        ModelWire.OpenAiCompatible,
        baseUrl.Trim(),
        string.Empty,
        0m,
        0m,
        Array.Empty<string>());

    private static string Slug(string value)
    {
        var cleaned = new string((value ?? string.Empty)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-')
            .ToArray());

        return cleaned.Trim('-') is { Length: > 0 } trimmed ? trimmed : "endpoint";
    }
}
