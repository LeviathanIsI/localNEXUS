using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Turns a pin type named in a manifest into a real <see cref="PinType"/>, and refuses anything
/// that is not one.
/// </summary>
/// <remarks>
/// This is the whole of the decision that extensions use the existing pin types and may not
/// invent their own, and it is deliberately the only place that decision is enforced.
/// <para>
/// The alternative was letting a manifest declare a new type. It was rejected because
/// compatibility between two declared types could only ever be decided by their names matching,
/// which means two unrelated extensions that both called something "Mesh" would be wired together
/// on the strength of a coincidence. That is precisely the scattered special case that keeping
/// one compatibility table exists to prevent, and <see cref="PinTypeCompatibility"/> stays a
/// single unchanged table because of this refusal.
/// </para>
/// <para>
/// The seam for changing this later, which is not built now because nothing needs it: the pin
/// type becomes a lookup keyed by name and seeded with today's enum values, and extensions add
/// entries to it at load. That is additive and does not disturb the table.
/// </para>
/// </remarks>
public static class ExtensionPinTypes
{
    /// <summary>Every pin type an extension may name, for error messages and for the manifest documentation.</summary>
    public static IReadOnlyList<string> Available { get; } =
        Enum.GetNames<PinType>();

    /// <summary>
    /// Parses a declared pin type.
    /// </summary>
    /// <exception cref="ExtensionException">The name is missing or is not an existing pin type.</exception>
    public static PinType Parse(string? declared, string typeKey, string pinName)
    {
        if (string.IsNullOrWhiteSpace(declared))
        {
            throw new ExtensionException(
                $"The pin '{pinName}' on node '{typeKey}' does not say what type it carries. " +
                $"It has to be one of: {string.Join(", ", Available)}.");
        }

        if (Enum.TryParse<PinType>(declared, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        throw new ExtensionException(
            $"The pin '{pinName}' on node '{typeKey}' asks for the type '{declared}', which does not exist. " +
            $"Extensions use the types the graph already has: {string.Join(", ", Available)}. " +
            "A new pin type would only match another extension's by name, so it is refused rather than guessed at.");
    }
}
