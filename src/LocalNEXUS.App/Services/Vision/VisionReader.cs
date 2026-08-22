using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Vision;

/// <summary>What a vision model made of an image.</summary>
/// <param name="Text">The structured extraction, as the model wrote it.</param>
/// <param name="Elapsed">How long it took, for the feed.</param>
public sealed record VisionReading(string Text, TimeSpan Elapsed);

/// <summary>
/// Reads an image and turns it into text the rest of the application can carry.
/// </summary>
/// <remarks>
/// The image never enters the graph, and that is the whole design. Pins are Text and Code, the
/// client sends strings, and the good local coding models are not multimodal. So a vision model
/// reads the image, produces structured text, and that text joins the request exactly as anything
/// typed does. Nothing marked untouchable is touched and the coder does not have to see a picture.
///
/// Its own HTTP call rather than the model client, for the same reason. Sending an image means
/// content parts rather than a string, and IModelClient carries a string because that is all the
/// graph ever needs; widening it for a step that happens before the graph starts would be changing
/// the run path to serve something outside it.
///
/// A local vision model needs two files rather than one. llama.cpp serves vision through a
/// multimodal projector alongside the weights, so llama-server has to be started with --mmproj
/// pointing at it, and a GGUF on its own will load and then refuse every image. That is why this is
/// configured as an address rather than as a file: a hosted model needs nothing, and a local one
/// needs a server somebody has already started correctly.
/// </remarks>
public sealed partial class VisionReader : ObservableObject
{
    /// <summary>What the credential store files the key under, when the endpoint wants one.</summary>
    public const string ProviderId = "vision-model";

    /// <summary>The largest image that is sent, in bytes.</summary>
    /// <remarks>
    /// A screenshot is a few hundred kilobytes and a photograph from a phone is several megabytes.
    /// The limit is here so that pasting the wrong thing fails immediately with a sentence rather
    /// than after a minute of uploading.
    /// </remarks>
    public const int MaximumBytes = 8 * 1024 * 1024;

    /// <summary>
    /// What the vision model is asked to do.
    /// </summary>
    /// <remarks>
    /// One prompt telling it to work out what it is looking at and extract accordingly, rather than
    /// one prompt per kind and something upstream guessing which to use. The model can see what the
    /// image is; nothing here can.
    ///
    /// Structured rather than descriptive, because the thing reading this next is a coder that
    /// should not have to recover a line number from a sentence.
    /// </remarks>
    public const string Prompt =
        "You are reading an image for a programmer. Work out what kind of image it is and extract "
        + "what matters, as fields rather than prose.\n\n"
        + "Start with a line saying KIND: followed by one of compiler-error, stack-trace, mockup, "
        + "screenshot, diagram, code, terminal, or other.\n\n"
        + "Then extract, using the shape that fits:\n"
        + "- compiler error: FILE, LINE, COLUMN, CODE, MESSAGE, and the offending source line if visible.\n"
        + "- stack trace: EXCEPTION, MESSAGE, then FRAME lines in order, each with its method, file and line.\n"
        + "- mockup or diagram: ELEMENTS, one per line, each with what it is, what it says, and where it sits "
        + "relative to the others. Then LAYOUT describing the arrangement in one line.\n"
        + "- code or terminal: transcribe it exactly, in a fenced block, and nothing else.\n"
        + "- anything else: FACTS, one per line, only what is actually visible.\n\n"
        + "Transcribe text exactly as it appears, including punctuation and casing. Do not guess at "
        + "anything you cannot read; write UNREADABLE for that field instead. Do not offer advice, "
        + "an opinion, or a summary.";

    private readonly AppConfig _config;
    private readonly ICredentialStore _credentials;
    private readonly HttpClient _http;

    public VisionReader(AppConfig config, ICredentialStore credentials, HttpClient http)
    {
        _config = config;
        _credentials = credentials;
        _http = http;
    }

    /// <summary>True when an address and a model id have both been set.</summary>
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(_config.VisionBaseUrl) && !string.IsNullOrWhiteSpace(_config.VisionModelId);

    /// <summary>What to say when there is no vision model, which is a state rather than a failure.</summary>
    public const string NotConfiguredMessage =
        "No vision model is configured, so the image was not read and nothing was added to your request. "
        + "Set one in Settings under Models. A hosted model needs an address, a model id and a key; a local "
        + "one needs llama-server started with a multimodal projector, because a GGUF on its own cannot see.";

    /// <summary>One line describing what is configured, for the settings panel.</summary>
    public string Status => IsConfigured
        ? $"{_config.VisionModelId} at {_config.VisionBaseUrl}. Paste or drop an image on the request box."
        : "Nothing configured, so pasting an image says so and does nothing else.";

    /// <summary>
    /// Reads one image and returns what the model extracted.
    /// </summary>
    /// <exception cref="VisionException">Nothing is configured, or the model could not be reached.</exception>
    public async Task<VisionReading> ReadAsync(byte[] image, string mediaType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (!IsConfigured)
        {
            throw new VisionException(NotConfiguredMessage);
        }

        if (image.Length == 0)
        {
            throw new VisionException("That image is empty.");
        }

        if (image.Length > MaximumBytes)
        {
            throw new VisionException(
                $"That image is {image.Length / (1024 * 1024)} MB, and the limit is {MaximumBytes / (1024 * 1024)} MB.");
        }

        var payload = new JsonObject
        {
            ["model"] = _config.VisionModelId,
            ["stream"] = false,
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "user",

                    // Content parts rather than a string, which is the whole reason this does not
                    // go through the model client. Every OpenAI compatible server that can see,
                    // including llama-server with a projector, takes this shape.
                    ["content"] = new JsonArray
                    {
                        new JsonObject { ["type"] = "text", ["text"] = Prompt },
                        new JsonObject
                        {
                            ["type"] = "image_url",
                            ["image_url"] = new JsonObject
                            {
                                ["url"] = $"data:{mediaType};base64,{Convert.ToBase64String(image)}"
                            }
                        }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint())
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        if (_credentials.Get(ProviderId) is { Length: > 0 } key)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new VisionException($"The vision model could not be reached: {ex.Message}", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            watch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                throw new VisionException(Explain(response.StatusCode, body));
            }

            var text = ReadReply(body);

            if (text.Trim().Length == 0)
            {
                throw new VisionException("The vision model answered with nothing.");
            }

            return new VisionReading(text.Trim(), watch.Elapsed);
        }
    }

    private string Endpoint()
    {
        var baseUrl = _config.VisionBaseUrl!.TrimEnd('/');

        return baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : $"{baseUrl}/chat/completions";
    }

    private static string Explain(System.Net.HttpStatusCode status, string body) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
            "The vision model refused the key. Check it in Settings under Models.",

        System.Net.HttpStatusCode.NotFound =>
            "The vision model's address answered but has no chat endpoint there. Check the base url.",

        System.Net.HttpStatusCode.BadRequest =>
            "The vision model refused the image. A local llama-server started without a multimodal "
            + $"projector answers this way, because it cannot see. It said: {Summarise(body)}",

        _ => $"The vision model failed with {(int)status}: {Summarise(body)}"
    };

    private static string ReadReply(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is not JsonObject payload
                || payload["choices"] is not JsonArray choices
                || choices.FirstOrDefault() is not JsonObject first
                || first["message"] is not JsonObject message)
            {
                return string.Empty;
            }

            return message["content"]?.GetValueKind() == JsonValueKind.String
                ? message["content"]!.GetValue<string>()
                : string.Empty;
        }
        catch (JsonException ex)
        {
            throw new VisionException($"The vision model answered with something that could not be read: {ex.Message}", ex);
        }
    }

    private static string Summarise(string value)
    {
        var flat = value.ReplaceLineEndings(" ").Trim();
        return flat.Length <= 200 ? flat : flat[..200] + "...";
    }

    /// <summary>Notifies that the configuration changed, so the panel and the box follow it.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(Status));
    }
}

/// <summary>An image that could not be read, worded for a person.</summary>
public sealed class VisionException : Exception
{
    public VisionException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
