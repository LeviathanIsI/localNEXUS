using System.IO;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Sends its input to a language model and emits the reply.
/// </summary>
/// <remarks>
/// One node type covers every role in a pipeline. A planning node and a coding node differ only
/// in their system prompt and their chosen model, so there is no reason for them to be separate
/// classes. Both providers share a single request path; the provider only decides which base URL
/// is used and whether an API key is attached.
/// </remarks>
public sealed partial class ModelNode : NodeBase
{
    /// <summary>Base URL used for every OpenRouter request.</summary>
    public const string OpenRouterBaseUrl = "https://openrouter.ai/api/v1";

    /// <summary>
    /// The starting system prompt. It is aimed at producing files that compile, because the end
    /// of the default pipeline writes straight into a Unity project. Change it per node to give
    /// a node a different role, for example planning rather than coding.
    /// </summary>
    public const string DefaultSystemPrompt =
        "You are an expert Unity C# engineer. Produce complete, compilable C# for Unity. "
        + "Output raw code only: no markdown code fences, no commentary, no explanation.";

    /// <summary>Whether this node uses a local GGUF or a hosted model.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    [NotifyPropertyChangedFor(nameof(IsOpenRouter))]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    private ModelProvider _provider = ModelProvider.Local;

    /// <summary>The GGUF selected from the catalog, when the provider is local.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    private LocalModelInfo? _selectedLocalModel;

    /// <summary>The model slug sent to OpenRouter, for example <c>anthropic/claude-sonnet-4</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    private string _openRouterModel = string.Empty;

    /// <summary>The system message sent with every request.</summary>
    [ObservableProperty]
    private string _systemPrompt = DefaultSystemPrompt;

    /// <summary>Sampling temperature.</summary>
    [ObservableProperty]
    private double _temperature = 0.4d;

    /// <summary>Upper bound on generated tokens.</summary>
    [ObservableProperty]
    private int _maxTokens = 4096;

    /// <summary>Context window requested when this node starts a llama-server.</summary>
    [ObservableProperty]
    private int _contextSize = LlamaLaunchOptions.DefaultContextSize;

    /// <summary>GPU layers requested when this node starts a llama-server.</summary>
    [ObservableProperty]
    private int _gpuLayers = LlamaLaunchOptions.DefaultGpuLayers;

    /// <summary>
    /// The endpoint root. Filled in automatically when the provider changes. Leaving it blank for
    /// a local model means "use the llama-server this application starts"; setting it points the
    /// node at a server that is already running somewhere else.
    /// </summary>
    [ObservableProperty]
    private string _baseUrl = string.Empty;

    /// <summary>Bearer token. Only sent when the provider is OpenRouter.</summary>
    [ObservableProperty]
    private string _apiKey = string.Empty;

    public ModelNode(ModelCatalog catalog)
        : base("Model")
    {
        Catalog = catalog;

        Prompt = AddInput("Text", PinType.Text);
        Completion = AddOutput("Code", PinType.Code);

        // A fresh node is usable straight away when the machine already has a model.
        SelectedLocalModel = catalog.Models.FirstOrDefault();
    }

    /// <summary>The GGUF files available for the local provider.</summary>
    public ModelCatalog Catalog { get; }

    /// <summary>Receives the text to send to the model.</summary>
    public Pin Prompt { get; }

    /// <summary>Carries the model reply onwards.</summary>
    public Pin Completion { get; }

    /// <inheritdoc />
    public override string TypeKey => "Model";

    /// <summary>True when the local provider is selected. Drives which settings are shown.</summary>
    public bool IsLocal => Provider == ModelProvider.Local;

    /// <summary>True when the OpenRouter provider is selected.</summary>
    public bool IsOpenRouter => Provider == ModelProvider.OpenRouter;

    /// <summary>The model this node will use, for display on the canvas.</summary>
    public string ModelDisplayName => Provider switch
    {
        ModelProvider.Local => SelectedLocalModel?.Name ?? "no model selected",
        ModelProvider.OpenRouter => string.IsNullOrWhiteSpace(OpenRouterModel) ? "no model slug" : OpenRouterModel,
        _ => "unknown"
    };

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var userContent = ctx.GetText(Prompt);
        if (string.IsNullOrWhiteSpace(userContent))
        {
            throw new InvalidOperationException(
                $"{Title} received no input. Connect something to its Text pin.");
        }

        var entry = ctx.Feed.Add(ActivityKind.ModelStream, $"{Title}  ({ModelDisplayName})", null, Id);
        var endpoint = await ResolveEndpointAsync(ctx, entry, ct).ConfigureAwait(false);

        var onToken = new DelegateProgress<string>(entry.Append);

        try
        {
            var result = await ctx.Services.ModelClient
                .StreamChatAsync(endpoint, SystemPrompt, userContent, Temperature, MaxTokens, onToken, ct)
                .ConfigureAwait(false);

            entry.Flush();
            entry.Detail = result.Summary;
            StatusMessage = result.Summary;

            if (string.IsNullOrWhiteSpace(result.Text))
            {
                throw new InvalidOperationException($"{Title} received an empty reply from {ModelDisplayName}.");
            }

            return NodeResult.FromPin(Completion, result.Text);
        }
        catch (OperationCanceledException)
        {
            entry.Flush();
            entry.Detail = "cancelled";
            throw;
        }
        catch (Exception)
        {
            entry.Flush();
            entry.Detail = "failed";
            throw;
        }
    }

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["provider"] = Provider.ToString(),
        ["localModelPath"] = SelectedLocalModel?.Path,
        ["openRouterModel"] = OpenRouterModel,
        ["systemPrompt"] = SystemPrompt,
        ["temperature"] = Temperature,
        ["maxTokens"] = MaxTokens,
        ["contextSize"] = ContextSize,
        ["gpuLayers"] = GpuLayers,
        ["baseUrl"] = BaseUrl,
        ["apiKey"] = ApiKey
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        // Provider is applied first because changing it rewrites the base URL.
        if (Enum.TryParse<ModelProvider>(settings["provider"]?.GetValue<string>(), out var provider))
        {
            Provider = provider;
        }

        var localPath = settings["localModelPath"]?.GetValue<string>();
        SelectedLocalModel = Catalog.FindByPath(localPath)
                             ?? (localPath is not null && File.Exists(localPath) ? new LocalModelInfo(localPath) : null);

        OpenRouterModel = settings["openRouterModel"]?.GetValue<string>() ?? string.Empty;
        SystemPrompt = settings["systemPrompt"]?.GetValue<string>() ?? DefaultSystemPrompt;
        Temperature = settings["temperature"]?.GetValue<double>() ?? 0.4d;
        MaxTokens = settings["maxTokens"]?.GetValue<int>() ?? 4096;
        ContextSize = settings["contextSize"]?.GetValue<int>() ?? LlamaLaunchOptions.DefaultContextSize;
        GpuLayers = settings["gpuLayers"]?.GetValue<int>() ?? LlamaLaunchOptions.DefaultGpuLayers;
        BaseUrl = settings["baseUrl"]?.GetValue<string>() ?? DefaultBaseUrlFor(Provider);
        ApiKey = settings["apiKey"]?.GetValue<string>() ?? string.Empty;
    }

    /// <summary>The base URL filled in when a provider is selected.</summary>
    public static string DefaultBaseUrlFor(ModelProvider provider) => provider switch
    {
        ModelProvider.OpenRouter => OpenRouterBaseUrl,
        _ => string.Empty
    };

    private async Task<ModelEndpoint> ResolveEndpointAsync(
        NodeExecutionContext ctx,
        ActivityEvent entry,
        CancellationToken ct)
    {
        if (Provider == ModelProvider.OpenRouter)
        {
            if (string.IsNullOrWhiteSpace(OpenRouterModel))
            {
                throw new InvalidOperationException($"{Title} has no OpenRouter model slug set.");
            }

            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                throw new InvalidOperationException($"{Title} has no OpenRouter API key set.");
            }

            var baseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? OpenRouterBaseUrl : BaseUrl;
            return new ModelEndpoint(baseUrl, OpenRouterModel, ApiKey);
        }

        var modelPath = SelectedLocalModel?.Path;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new InvalidOperationException(
                $"{Title} has no local model selected. Drop a GGUF file into the models folder or add a folder from the settings panel.");
        }

        var modelId = Path.GetFileNameWithoutExtension(modelPath);

        // An explicit base URL means the user is pointing at their own server, so nothing is spawned.
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new ModelEndpoint(BaseUrl, modelId);
        }

        var status = new DelegateProgress<string>(message =>
        {
            entry.Detail = message;
            StatusMessage = message;
        });

        var launchOptions = new LlamaLaunchOptions
        {
            ContextSize = ContextSize,
            GpuLayers = GpuLayers
        };

        var managedBaseUrl = await ctx.Services.LlamaServers
            .EnsureServerAsync(modelPath, launchOptions, status, ct)
            .ConfigureAwait(false);

        return new ModelEndpoint(managedBaseUrl, modelId);
    }

    partial void OnProviderChanged(ModelProvider value) => BaseUrl = DefaultBaseUrlFor(value);
}
