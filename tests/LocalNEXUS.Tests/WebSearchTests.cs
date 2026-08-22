using System.Net;
using System.Net.Http;
using LocalNEXUS.App.Services.Search;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Web search: when it is offered, what it sends, and what it does with what comes back.
/// </summary>
/// <remarks>
/// Nothing here reaches the network. What is worth pinning is the decisions: that no key means no
/// search anywhere, that the request carries the header Brave expects, that a failure comes back as
/// something a person can act on, and that what the model is handed is snippets rather than pages.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class WebSearchTests
{
    /// <summary>An HTTP client that answers from a script and records what it was asked.</summary>
    private sealed class Canned : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public Canned(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>What Brave's web search returns, in the shape its documentation describes.</summary>
    private const string TwoResults = """
        {
          "web": {
            "results": [
              {
                "title": "String.Split Method",
                "url": "https://learn.microsoft.com/dotnet/api/system.string.split",
                "description": "Returns a string array that contains the substrings."
              },
              {
                "title": "Deprecated in 8.0",
                "url": "https://example.invalid/notes",
                "description": "This overload was removed."
              }
            ]
          }
        }
        """;

    private static (WebSearchService Search, Canned Handler, InMemoryCredentialStore Store) Build(
        HttpStatusCode status = HttpStatusCode.OK,
        string body = TwoResults,
        string? key = "a-key")
    {
        var store = new InMemoryCredentialStore();

        if (key is not null)
        {
            store.Set(WebSearchService.ProviderId, key);
        }

        var handler = new Canned(status, body);
        return (new WebSearchService(store, new HttpClient(handler)), handler, store);
    }

    /// <summary>No key means nothing is offered, anywhere.</summary>
    /// <remarks>
    /// The toggle is bound to this, so an installation without a key has no checkbox rather than a
    /// disabled one. A control for something unavailable teaches people it does not work.
    /// </remarks>
    [Fact]
    public void WithoutAKeyThereIsNoSearch()
    {
        var (search, _, _) = Build(key: null);

        Assert.Equal(SearchAvailability.NoKey, search.Availability);
        Assert.False(search.HasKey);

        // Even asked for, because the switch is not what decides availability.
        search.EnabledForThisRun = true;
        Assert.False(search.IsOfferedThisRun);
    }

    /// <summary>A key alone is not enough either. The send has to have asked.</summary>
    [Fact]
    public void AKeyAloneDoesNotTurnSearchOn()
    {
        var (search, _, _) = Build();

        Assert.True(search.HasKey);
        Assert.False(search.IsOfferedThisRun);

        search.EnabledForThisRun = true;
        Assert.True(search.IsOfferedThisRun);
    }

    /// <summary>Adding a key makes it available without anything being rebuilt.</summary>
    [Fact]
    public void AddingAKeyMakesItAvailable()
    {
        var (search, _, _) = Build(key: null);

        Assert.False(search.HasKey);

        search.SetKey("added-later");
        Assert.True(search.HasKey);

        search.SetKey(null);
        Assert.False(search.HasKey);
    }

    /// <summary>The request carries the header Brave authenticates with, and the query.</summary>
    [Fact]
    public async Task TheRequestIsWhatBraveExpects()
    {
        var (search, handler, _) = Build();

        await search.SearchAsync("string.split dotnet 8", CancellationToken.None);

        Assert.NotNull(handler.Last);
        Assert.Equal(HttpMethod.Get, handler.Last!.Method);
        Assert.Contains("api.search.brave.com", handler.Last.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains("string.split", handler.Last.RequestUri.Query, StringComparison.OrdinalIgnoreCase);

        Assert.True(handler.Last.Headers.Contains("X-Subscription-Token"));
        Assert.Equal("a-key", handler.Last.Headers.GetValues("X-Subscription-Token").Single());
    }

    /// <summary>Results are read as title, link and snippet.</summary>
    [Fact]
    public async Task ResultsAreReadAsSnippets()
    {
        var (search, _, _) = Build();

        var results = await search.SearchAsync("anything", CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.Equal("String.Split Method", results[0].Title);
        Assert.Equal("https://learn.microsoft.com/dotnet/api/system.string.split", results[0].Url);
        Assert.Equal("Returns a string array that contains the substrings.", results[0].Snippet);
    }

    /// <summary>
    /// What the model is handed is the snippets and nothing else.
    /// </summary>
    /// <remarks>
    /// Snippets rather than pages is the decision this feature turns on. Fetching the page behind a
    /// result would put arbitrary web content into the context that writes files, which is a larger
    /// decision and is deliberately not made here.
    /// </remarks>
    [Fact]
    public void WhatTheModelReadsIsTitleLinkAndExtract()
    {
        var text = WebSearchService.Format("split", new[]
        {
            new SearchResult("A title", "https://example.invalid/a", "An extract.")
        });

        Assert.Contains("A title", text, StringComparison.Ordinal);
        Assert.Contains("https://example.invalid/a", text, StringComparison.Ordinal);
        Assert.Contains("An extract.", text, StringComparison.Ordinal);
    }

    /// <summary>Nothing found is said, rather than looking like a failure.</summary>
    [Fact]
    public void NothingFoundIsSaidPlainly()
    {
        var text = WebSearchService.Format("nothing at all", Array.Empty<SearchResult>());

        Assert.Contains("No results", text, StringComparison.Ordinal);
    }

    /// <summary>A refused key says to check the key, not that the search failed.</summary>
    [Fact]
    public async Task ARefusedKeySaysToCheckTheKey()
    {
        var (search, _, _) = Build(HttpStatusCode.Unauthorized, "{}");

        var ex = await Assert.ThrowsAsync<SearchException>(
            () => search.SearchAsync("anything", CancellationToken.None));

        Assert.Contains("key was refused", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A rate limit says what the limit is.</summary>
    [Fact]
    public async Task ARateLimitSaysWhatTheLimitIs()
    {
        var (search, _, _) = Build(HttpStatusCode.TooManyRequests, "{}");

        var ex = await Assert.ThrowsAsync<SearchException>(
            () => search.SearchAsync("anything", CancellationToken.None));

        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Searching with no key at all says where to get one.</summary>
    [Fact]
    public async Task SearchingWithNoKeySaysWhereToGetOne()
    {
        var (search, _, _) = Build(key: null);

        var ex = await Assert.ThrowsAsync<SearchException>(
            () => search.SearchAsync("anything", CancellationToken.None));

        Assert.Contains(WebSearchService.KeyUrl, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>An empty query is refused before anything is sent.</summary>
    [Fact]
    public async Task AnEmptyQueryIsRefused()
    {
        var (search, handler, _) = Build();

        await Assert.ThrowsAsync<SearchException>(
            () => search.SearchAsync("   ", CancellationToken.None));

        Assert.Null(handler.Last);
    }

    /// <summary>An answer that will not parse is reported rather than thrown raw.</summary>
    [Fact]
    public async Task AnUnreadableAnswerIsReported()
    {
        var (search, _, _) = Build(HttpStatusCode.OK, "{ not json");

        await Assert.ThrowsAsync<SearchException>(
            () => search.SearchAsync("anything", CancellationToken.None));
    }

    /// <summary>An answer with no results at all is empty rather than an error.</summary>
    [Fact]
    public async Task AnAnswerWithNoResultsIsEmpty()
    {
        var (search, _, _) = Build(HttpStatusCode.OK, """{"query":{"original":"x"}}""");

        Assert.Empty(await search.SearchAsync("x", CancellationToken.None));
    }

    /// <summary>
    /// The tool the model is offered describes when to reach for it and says it does not fetch.
    /// </summary>
    /// <remarks>
    /// The description is the only thing a model reads before choosing a tool, so it is the whole
    /// of whether search is used when it should be and left alone when it should not.
    /// </remarks>
    [Fact]
    public void TheToolSaysWhenToUseItAndWhatItDoesNotDo()
    {
        var tool = WebSearchService.Tool;

        Assert.Equal("web_search", tool.Name);
        Assert.Equal(WebSearchService.OwnerId, tool.ExtensionId);

        Assert.Contains("deprecated", tool.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not fetch pages", tool.Description, StringComparison.OrdinalIgnoreCase);

        var schema = tool.ParametersSchema!;

        Assert.Equal("object", schema["type"]!.GetValue<string>());
        Assert.NotNull(schema["properties"]!["query"]);
    }

    /// <summary>
    /// The tool's owner is this application, which is what routes a call away from the extensions.
    /// </summary>
    /// <remarks>
    /// A tool carries the id of whatever provides it so a call can be routed back. This one carries
    /// a name no extension can have, and the model node checks for it before asking the extension
    /// host about a tool the host has never heard of.
    /// </remarks>
    [Fact]
    public void TheToolIsOwnedByTheApplicationRatherThanAnExtension()
        => Assert.StartsWith("localnexus.", WebSearchService.OwnerId, StringComparison.Ordinal);
}
