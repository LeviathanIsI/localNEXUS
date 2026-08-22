using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Vision;

/// <summary>Where the model that reads images is.</summary>
public enum VisionSource
{
    /// <summary>Nothing is configured, so pasting an image says so and does nothing else.</summary>
    None,

    /// <summary>A GGUF on this machine, served by this application when an image arrives.</summary>
    Local,

    /// <summary>An address somebody else is serving, hosted or their own.</summary>
    Endpoint
}

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
/// A local model is picked from the model folders like any other and served by whichever runtime
/// serves it, which for a GGUF is llama-server. The one thing that makes vision different is a
/// launch argument: llama.cpp reads images through a multimodal projector published beside the
/// weights, and a vision GGUF started without one loads perfectly and then answers 400 to every
/// image. That argument is worked out by <see cref="VisionProjectorLocator"/> rather than asked of
/// the user, and a model with no projector beside it is refused when it is chosen rather than when
/// the first image arrives.
///
/// The server starts the first time an image is pasted, not at launch, because somebody who never
/// pastes one should not be holding a model on the GPU all session.
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
    private readonly RuntimeResolver _runtimes;

    public VisionReader(AppConfig config, ICredentialStore credentials, HttpClient http, RuntimeResolver runtimes)
    {
        _config = config;
        _credentials = credentials;
        _http = http;
        _runtimes = runtimes;
    }

    /// <summary>
    /// Which of the two ways in is configured, if either.
    /// </summary>
    /// <remarks>
    /// A chosen file wins over an address, because picking one is the more deliberate act of the
    /// two and an address left behind from an earlier arrangement should not quietly outrank it.
    /// </remarks>
    public VisionSource Source
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_config.VisionModelPath))
            {
                return VisionSource.Local;
            }

            return !string.IsNullOrWhiteSpace(_config.VisionBaseUrl)
                   && !string.IsNullOrWhiteSpace(_config.VisionModelId)
                ? VisionSource.Endpoint
                : VisionSource.None;
        }
    }

    /// <summary>True when there is somewhere to send an image.</summary>
    public bool IsConfigured => Source != VisionSource.None;

    /// <summary>What to say when there is no vision model, which is a state rather than a failure.</summary>
    public const string NotConfiguredMessage =
        "No vision model is configured, so the image was not read and nothing was added to your request. "
        + "Set one in Settings under Models. Either pick a local vision model from your model folders, which "
        + "this application will start for you, or give the address and model id of one somebody else is "
        + "serving. A local vision model is published as two files, the weights and an mmproj projector; both "
        + "need to be in the same folder, and the projector is found on its own.";

    /// <summary>One line describing what is configured, for the settings panel.</summary>
    public string Status => Source switch
    {
        VisionSource.Local => LocalStatus(),

        VisionSource.Endpoint =>
            $"{_config.VisionModelId} at {_config.VisionBaseUrl}. Paste or drop an image on the request box.",

        _ => "Nothing configured, so pasting an image says so and does nothing else."
    };

    private string LocalStatus()
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(_config.VisionModelPath!);
        var lookup = VisionProjectorLocator.Locate(_config.VisionModelPath);

        return lookup.IsUsable
            ? $"{name}, started here when you paste an image. Projector: {lookup.Message}"
            : $"{name} cannot be used. {lookup.Message}";
    }

    /// <summary>
    /// Reads one image and returns what the model extracted.
    /// </summary>
    /// <param name="image">The image bytes, however they arrived.</param>
    /// <param name="mediaType">What kind of image it is, for the data uri.</param>
    /// <param name="status">Receives progress while a local server starts, so the wait is visible.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <exception cref="VisionException">Nothing is configured, or the model could not be reached.</exception>
    public async Task<VisionReading> ReadAsync(
        byte[] image,
        string mediaType,
        IProgress<string>? status,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (Source == VisionSource.None)
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

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var target = await ResolveAsync(status, ct).ConfigureAwait(false);

        var payload = new JsonObject
        {
            ["model"] = target.ModelId,
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

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint(target.BaseUrl))
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };

        if (_credentials.Get(ProviderId) is { Length: > 0 } key)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }

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

    /// <summary>
    /// Works out where to send the image, starting a local server if that is what is configured.
    /// </summary>
    /// <remarks>
    /// The projector is looked for every time rather than remembered, because it lives beside the
    /// weights and a remembered path is one that goes stale when the folder moves. Reading a header
    /// costs nothing next to loading a model onto a card.
    /// </remarks>
    private async Task<RuntimeEndpoint> ResolveAsync(IProgress<string>? status, CancellationToken ct)
    {
        if (Source == VisionSource.Endpoint)
        {
            return new RuntimeEndpoint(_config.VisionBaseUrl!.TrimEnd('/'), _config.VisionModelId!);
        }

        var path = _config.VisionModelPath!;
        var lookup = VisionProjectorLocator.Locate(path);

        if (!lookup.IsUsable)
        {
            throw new VisionException(lookup.Message);
        }

        status?.Report($"Starting the vision model {System.IO.Path.GetFileNameWithoutExtension(path)}");

        try
        {
            return await _runtimes
                .ServeAsync(path, new ModelRuntimeOptions { ProjectorPath = lookup.Path }, status, ct)
                .ConfigureAwait(false);
        }
        catch (ModelClientException ex)
        {
            throw new VisionException($"The vision model could not be started: {ex.Message}", ex);
        }
    }

    private static string Endpoint(string baseUrl)
    {
        var trimmed = baseUrl.TrimEnd('/');

        return trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/chat/completions";
    }

    private static string Explain(System.Net.HttpStatusCode status, string body) => status switch
    {
        System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden =>
            "The vision model refused the key. Check it in Settings under Models.",

        System.Net.HttpStatusCode.NotFound =>
            "The vision model's address answered but has no chat endpoint there. Check the base url.",

        System.Net.HttpStatusCode.BadRequest =>
            "The vision model refused the image. A server started without a multimodal projector answers "
            + $"this way, because it cannot see. It said: {Summarise(body)}",

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
        OnPropertyChanged(nameof(Source));
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
