using System.Globalization;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// What a call cost, or at most could cost.
/// </summary>
/// <remarks>
/// Two different numbers live in this type and confusing them is the failure worth designing
/// against. What a call cost is arithmetic on tokens that were actually counted. What a call
/// could cost is a ceiling, computed before anything runs, from the input plus the most the
/// model is allowed to produce. The second is always at least the first and usually well above
/// it, because models rarely write until they are cut off.
/// </remarks>
public static class RunCost
{
    /// <summary>What a completed call cost at a provider's rates.</summary>
    public static decimal Actual(CloudProvider provider, int promptTokens, int completionTokens)
        => (promptTokens / 1_000_000m * provider.InputPerMillion)
           + (completionTokens / 1_000_000m * provider.OutputPerMillion);

    /// <summary>
    /// The most one call could cost, before it runs.
    /// </summary>
    /// <param name="provider">Whose rates apply.</param>
    /// <param name="promptCharacters">How much text is going in.</param>
    /// <param name="maxTokens">The most the node will let the model produce.</param>
    /// <remarks>
    /// The input is estimated from characters because the real count is only known once the
    /// provider has tokenised it, and running a tokeniser per provider to price a warning would
    /// cost more than the warning is worth. Four characters per token is the rough industry
    /// figure and is close enough for a ceiling that is already approximate.
    /// </remarks>
    public static decimal Ceiling(CloudProvider provider, int promptCharacters, int maxTokens)
    {
        var estimatedPrompt = promptCharacters / 4;
        return Actual(provider, estimatedPrompt, Math.Max(0, maxTokens));
    }

    /// <summary>
    /// A money figure as the interface shows it.
    /// </summary>
    /// <remarks>
    /// Small amounts get more decimal places rather than rounding to zero, because a run that
    /// cost a fraction of a cent should not read as free when the next hundred will not be.
    /// </remarks>
    public static string Format(decimal amount)
    {
        if (amount <= 0m)
        {
            return "$0.00";
        }

        if (amount < 0.01m)
        {
            return "$" + amount.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        return "$" + amount.ToString("0.00", CultureInfo.InvariantCulture);
    }

    /// <summary>True when this provider has rates worth showing a figure for.</summary>
    public static bool HasRates(CloudProvider? provider)
        => provider is not null && (provider.InputPerMillion > 0m || provider.OutputPerMillion > 0m);
}
