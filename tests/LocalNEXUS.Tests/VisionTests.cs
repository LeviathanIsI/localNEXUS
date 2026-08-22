using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Vision;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Reading an image into text, which is the only way an image reaches a text pipeline.
/// </summary>
/// <remarks>
/// The design this holds to is that the image never enters the graph. What is worth pinning is that
/// no vision model means nothing happens and it is said, that what is sent is the content part
/// shape a server that can see expects, and that a local server without a projector is explained
/// rather than reported as a mystery.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class VisionTests
{
    private sealed class Canned : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;

        public Canned(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
        }

        public string? LastBody { get; private set; }

        public HttpRequestMessage? Last { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            LastBody = request.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult();

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }

    /// <summary>A compiler error read into fields, which is what the prompt asks for.</summary>
    private const string Extracted = """
        {
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": "KIND: compiler-error\nFILE: Basket.cs\nLINE: 42\nCOLUMN: 17\nCODE: CS0246\nMESSAGE: The type or namespace name 'Money' could not be found"
              }
            }
          ]
        }
        """;

    private static (VisionReader Vision, Canned Handler, AppConfig Config) Build(
        HttpStatusCode status = HttpStatusCode.OK,
        string body = Extracted,
        bool configured = true)
    {
        var config = new AppConfig();

        if (configured)
        {
            config.VisionBaseUrl = "http://127.0.0.1:8081/v1";
            config.VisionModelId = "qwen2-vl";
        }

        var handler = new Canned(status, body);

        return (new VisionReader(config, new InMemoryCredentialStore(), new HttpClient(handler)), handler, config);
    }

    private static byte[] AnImage() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 };

    /// <summary>
    /// No vision model means no image handling, said plainly.
    /// </summary>
    /// <remarks>
    /// And the sentence says what a local one needs, because a GGUF that loads and then refuses
    /// every image is the confusing failure this is most likely to produce.
    /// </remarks>
    [Fact]
    public async Task WithoutAVisionModelNothingHappensAndItIsSaid()
    {
        var (vision, handler, _) = Build(configured: false);

        Assert.False(vision.IsConfigured);

        var ex = await Assert.ThrowsAsync<VisionException>(
            () => vision.ReadAsync(AnImage(), "image/png", CancellationToken.None));

        Assert.Equal(VisionReader.NotConfiguredMessage, ex.Message);
        Assert.Contains("projector", ex.Message, StringComparison.OrdinalIgnoreCase);

        // And nothing was sent anywhere.
        Assert.Null(handler.Last);
    }

    /// <summary>An address alone is not a configured model.</summary>
    [Fact]
    public void BothAnAddressAndAModelAreNeeded()
    {
        var (vision, _, config) = Build(configured: false);

        config.VisionBaseUrl = "http://127.0.0.1:8081/v1";
        Assert.False(vision.IsConfigured);

        config.VisionModelId = "something";
        Assert.True(vision.IsConfigured);
    }

    /// <summary>
    /// The request carries the image as a content part, which is what a server that can see takes.
    /// </summary>
    /// <remarks>
    /// The reason this does not go through the model client. That client sends a string, because a
    /// string is all the graph ever needs, and widening it for a step that happens before the graph
    /// starts would be changing the run path to serve something outside it.
    /// </remarks>
    [Fact]
    public async Task TheImageIsSentAsAContentPart()
    {
        var (vision, handler, _) = Build();

        await vision.ReadAsync(AnImage(), "image/png", CancellationToken.None);

        Assert.NotNull(handler.LastBody);

        var payload = (JsonObject)JsonNode.Parse(handler.LastBody!)!;
        var content = (JsonArray)payload["messages"]![0]!["content"]!;

        Assert.Equal(2, content.Count);
        Assert.Equal("text", content[0]!["type"]!.GetValue<string>());
        Assert.Equal("image_url", content[1]!["type"]!.GetValue<string>());

        var url = content[1]!["image_url"]!["url"]!.GetValue<string>();

        Assert.StartsWith("data:image/png;base64,", url, StringComparison.Ordinal);
        Assert.Equal("qwen2-vl", payload["model"]!.GetValue<string>());
    }

    /// <summary>The endpoint is completed for a base url that names no path.</summary>
    [Fact]
    public async Task TheChatEndpointIsAppendedWhenItIsMissing()
    {
        var (vision, handler, _) = Build();

        await vision.ReadAsync(AnImage(), "image/png", CancellationToken.None);

        Assert.EndsWith("/v1/chat/completions", handler.Last!.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The prompt asks for a kind and for fields, not for prose.
    /// </summary>
    /// <remarks>
    /// The whole value of this step. A coder should not have to recover a line number from a
    /// sentence, and one prompt covering every kind is what lets the model decide what it is
    /// looking at rather than something upstream guessing.
    /// </remarks>
    [Fact]
    public void ThePromptAsksForAKindAndForFields()
    {
        Assert.Contains("KIND:", VisionReader.Prompt, StringComparison.Ordinal);
        Assert.Contains("compiler-error", VisionReader.Prompt, StringComparison.Ordinal);
        Assert.Contains("stack-trace", VisionReader.Prompt, StringComparison.Ordinal);
        Assert.Contains("mockup", VisionReader.Prompt, StringComparison.Ordinal);

        Assert.Contains("fields rather than prose", VisionReader.Prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UNREADABLE", VisionReader.Prompt, StringComparison.Ordinal);

        // And it is told not to editorialise, because the next reader is a coder.
        Assert.Contains("Do not offer advice", VisionReader.Prompt, StringComparison.Ordinal);
    }

    /// <summary>What comes back is the extraction, ready to join a request.</summary>
    [Fact]
    public async Task TheExtractionComesBackAsText()
    {
        var (vision, _, _) = Build();

        var reading = await vision.ReadAsync(AnImage(), "image/png", CancellationToken.None);

        Assert.StartsWith("KIND: compiler-error", reading.Text, StringComparison.Ordinal);
        Assert.Contains("LINE: 42", reading.Text, StringComparison.Ordinal);
        Assert.Contains("CS0246", reading.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A server that cannot see is explained rather than reported as a mystery.
    /// </summary>
    /// <remarks>
    /// llama-server started without a projector answers a bad request to an image, which is the
    /// single most likely way this is set up wrongly.
    /// </remarks>
    [Fact]
    public async Task AServerWithoutAProjectorIsExplained()
    {
        var (vision, _, _) = Build(HttpStatusCode.BadRequest, """{"error":"image input not supported"}""");

        var ex = await Assert.ThrowsAsync<VisionException>(
            () => vision.ReadAsync(AnImage(), "image/png", CancellationToken.None));

        Assert.Contains("projector", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A refused key says to check the key.</summary>
    [Fact]
    public async Task ARefusedKeySaysToCheckTheKey()
    {
        var (vision, _, _) = Build(HttpStatusCode.Unauthorized, "{}");

        var ex = await Assert.ThrowsAsync<VisionException>(
            () => vision.ReadAsync(AnImage(), "image/png", CancellationToken.None));

        Assert.Contains("refused the key", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An image too large to be worth sending is refused before it is sent.</summary>
    [Fact]
    public async Task AnOversizedImageIsRefusedBeforeItIsSent()
    {
        var (vision, handler, _) = Build();

        var ex = await Assert.ThrowsAsync<VisionException>(
            () => vision.ReadAsync(new byte[VisionReader.MaximumBytes + 1], "image/png", CancellationToken.None));

        Assert.Contains("limit", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(handler.Last);
    }

    /// <summary>An empty image is refused before it is sent.</summary>
    [Fact]
    public async Task AnEmptyImageIsRefused()
    {
        var (vision, handler, _) = Build();

        await Assert.ThrowsAsync<VisionException>(
            () => vision.ReadAsync(Array.Empty<byte>(), "image/png", CancellationToken.None));

        Assert.Null(handler.Last);
    }

    /// <summary>A model that answers with nothing is reported rather than adding nothing silently.</summary>
    [Fact]
    public async Task AnEmptyAnswerIsReported()
    {
        var (vision, _, _) = Build(HttpStatusCode.OK, """{"choices":[{"message":{"content":"   "}}]}""");

        var ex = await Assert.ThrowsAsync<VisionException>(
            () => vision.ReadAsync(AnImage(), "image/png", CancellationToken.None));

        Assert.Contains("answered with nothing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
