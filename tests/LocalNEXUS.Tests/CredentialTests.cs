using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Where a hosted provider's key lives, and where it must never appear.
/// </summary>
/// <remarks>
/// The rule that matters is not how the key is encrypted. It is that a node does not carry one, so
/// a graph shared with somebody, committed to a repository, or attached to an issue cannot contain
/// it. A node names a provider and the key is looked up when a run needs it, which is what these
/// tests hold in place.
///
/// The real store, <see cref="DpapiCredentialStore"/>, is not exercised here. Its file path is a
/// static reading from the user's own application data, so constructing one reads their real keys
/// and setting one overwrites their real file. That is reported as a finding rather than worked
/// around: the suite does not write where the application's own data lives.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class CredentialTests
{
    private const string Secret = "sk-do-not-let-this-reach-a-file";

    /// <summary>
    /// A saved graph contains no key, whatever the node was configured with.
    /// </summary>
    /// <remarks>
    /// The failure this exists for is somebody sharing a graph and their key going with it. It is
    /// checked against the whole serialized document rather than against a named field, because a
    /// field added later would slip past a check that only knew the fields that existed today.
    /// </remarks>
    [Fact]
    public void ASavedGraphNeverContainsAKey()
    {
        using var services = TestServices.Create();
        services.Services.Credentials!.Set("openrouter", Secret);

        var model = (ModelNode)services.Factory.Create("Model");
        model.CloudProviderId = "openrouter";
        model.CloudModelId = "anthropic/claude-3.5-sonnet";

        var graph = new GraphModel();
        graph.AddNode(model);

        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "localnexus-tests",
            Guid.NewGuid().ToString("N") + GraphSerializer.FileExtension);

        var serializer = new GraphSerializer(services.Factory);

        try
        {
            serializer.Save(graph, path);

            var json = System.IO.File.ReadAllText(path);

            Assert.DoesNotContain(Secret, json, StringComparison.Ordinal);
            Assert.Contains("openrouter", json, StringComparison.Ordinal);
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    /// <summary>A node's own settings carry the provider and not the key.</summary>
    [Fact]
    public void ANodeCarriesTheProviderNotTheKey()
    {
        using var services = TestServices.Create();
        services.Services.Credentials!.Set("openrouter", Secret);

        var model = (ModelNode)services.Factory.Create("Model");
        model.CloudProviderId = "openrouter";

        var settings = model.SaveSettings();

        Assert.Equal("openrouter", settings["cloudProvider"]?.GetValue<string>());
        Assert.DoesNotContain(Secret, settings.ToJsonString(), StringComparison.Ordinal);

        // And no field named as somewhere a key would go, so a later addition is caught too.
        // Deliberately not a match on "token", because a token count is a setting and belongs here.
        Assert.DoesNotContain(
            settings.Select(p => p.Key),
            name => name.Contains("apikey", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("api_key", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("password", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The store is what a run asks, and it answers by provider.</summary>
    [Fact]
    public void TheKeyIsLookedUpByProvider()
    {
        using var services = TestServices.Create();
        var store = services.Services.Credentials!;

        Assert.False(store.Has("openrouter"));
        Assert.Null(store.Get("openrouter"));

        store.Set("openrouter", Secret);

        Assert.True(store.Has("openrouter"));
        Assert.Equal(Secret, store.Get("openrouter"));
        Assert.Contains("openrouter", store.ConfiguredProviders());
    }

    /// <summary>Removing a key removes it, and the provider stops being configured.</summary>
    [Fact]
    public void RemovingAKeyRemovesIt()
    {
        using var services = TestServices.Create();
        var store = services.Services.Credentials!;

        store.Set("openrouter", Secret);
        store.Remove("openrouter");

        Assert.False(store.Has("openrouter"));
        Assert.Empty(store.ConfiguredProviders());
    }

    /// <summary>An empty key is a removal rather than a stored empty string.</summary>
    /// <remarks>
    /// Clearing the box in settings and storing "" would leave the provider configured with a key
    /// that cannot work, which fails at the request rather than at the point it was cleared.
    /// </remarks>
    [Fact]
    public void AnEmptyKeyIsARemoval()
    {
        using var services = TestServices.Create();
        var store = services.Services.Credentials!;

        store.Set("openrouter", Secret);
        store.Set("openrouter", "   ");

        Assert.False(store.Has("openrouter"));
    }

    /// <summary>Loading a graph does not resurrect a key from an older document that had one.</summary>
    [Fact]
    public void AnOldGraphCarryingAKeyDoesNotRestoreIt()
    {
        using var services = TestServices.Create();

        var model = (ModelNode)services.Factory.Create("Model");
        var settings = model.SaveSettings();

        // What a build before the credential store wrote.
        settings["apiKey"] = Secret;

        model.LoadSettings(settings);

        Assert.DoesNotContain(Secret, model.SaveSettings().ToJsonString(), StringComparison.Ordinal);
        Assert.False(services.Services.Credentials!.Has("openrouter"));
    }
}
