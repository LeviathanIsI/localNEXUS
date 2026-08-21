namespace LocalNEXUS.App.Services.Credentials;

/// <summary>
/// Holds the API keys for hosted providers.
/// </summary>
/// <remarks>
/// Keyed by provider rather than by node, which is the whole point. A key belongs to an account,
/// not to a box on a canvas, so pointing five nodes at Anthropic is one key rather than five
/// copies of one.
///
/// It is also what makes a graph shareable. A node records that it uses Anthropic; the key is
/// looked up when the run needs it and never travels with the file.
/// </remarks>
public interface ICredentialStore
{
    /// <summary>The key for a provider, or null when none has been set.</summary>
    string? Get(string providerId);

    /// <summary>True when a key exists, without decrypting or handling it.</summary>
    bool Has(string providerId);

    /// <summary>Stores a key. An empty or blank value removes it.</summary>
    void Set(string providerId, string? key);

    /// <summary>Forgets a key.</summary>
    void Remove(string providerId);

    /// <summary>Every provider id that currently has a key, for the settings list.</summary>
    IReadOnlyCollection<string> ConfiguredProviders();
}
