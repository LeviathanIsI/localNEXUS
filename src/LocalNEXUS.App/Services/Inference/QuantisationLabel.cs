using System.Text.RegularExpressions;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Reads the quantization out of a model's name.
/// </summary>
/// <remarks>
/// The convention comes from llama.cpp and is followed almost everywhere GGUF files are
/// published: the type is a suffix on the file name, such as <c>Q4_K_M</c>, <c>IQ3_XXS</c> or
/// <c>F16</c>. A name that does not carry one is reported as not stated rather than guessed at,
/// because a wrong quantization on screen is worse than an absent one: it is the number somebody
/// uses to judge whether a model will fit.
/// </remarks>
public static class QuantisationLabel
{
    /// <summary>What a model says when its name does not carry a quantization.</summary>
    public const string Unknown = "not stated";

    private static readonly Regex Pattern = new(
        @"(?<![A-Za-z0-9])(?<label>IQ\d+(_[A-Z0-9]+)*|Q\d+(_[A-Z0-9]+)*|BF16|F16|F32)(?![A-Za-z0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>The quantization in a name, upper cased, or <see cref="Unknown"/>.</summary>
    public static string Read(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Unknown;
        }

        // The last match wins: a name like "qwen2.5-0.5b-instruct-q4_k_m" has a parameter count
        // earlier in it that can look like a quantization, and the type is conventionally last.
        var matches = Pattern.Matches(name);

        return matches.Count == 0
            ? Unknown
            : matches[^1].Groups["label"].Value.ToUpperInvariant();
    }
}
