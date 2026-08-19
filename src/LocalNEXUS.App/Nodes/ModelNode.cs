using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Distributed;
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
/// classes. Every provider shares a single request path over the OpenAI compatible API; where
/// inference physically happens, one machine or several, is decided during resolution and the
/// graph does not care.
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

    /// <summary>Where this node's requests go.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    [NotifyPropertyChangedFor(nameof(IsSelfHosted))]
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

    /// <summary>The model id sent to a self hosted server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    private string _selfHostedModelId = string.Empty;

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
    /// Splits the model across sources even when it fits on this machine. Distribution is a
    /// capability unlock rather than a speedup, so this exists for testing the split path with
    /// a small model, not as a performance setting.
    /// </summary>
    [ObservableProperty]
    private bool _forceSplit;

    /// <summary>
    /// Manual split proportions, comma separated with dot decimals, one value per source in
    /// the plan's order: remote sources first, this machine last. Blank means automatic by
    /// declared memory.
    /// </summary>
    [ObservableProperty]
    private string _splitProportions = string.Empty;

    /// <summary>
    /// The endpoint root. Filled in automatically when the provider changes. Leaving it blank
    /// for a local model means "use servers this application starts"; setting it points the
    /// node at a server that is already running somewhere else and nothing is spawned.
    /// </summary>
    [ObservableProperty]
    private string _baseUrl = string.Empty;

    /// <summary>Bearer token. Sent to OpenRouter, and to a self hosted server when set.</summary>
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

    /// <summary>True when the self hosted provider is selected.</summary>
    public bool IsSelfHosted => Provider == ModelProvider.SelfHosted;

    /// <summary>True when the OpenRouter provider is selected.</summary>
    public bool IsOpenRouter => Provider == ModelProvider.OpenRouter;

    /// <summary>The model this node will use, for display on the canvas.</summary>
    public string ModelDisplayName => Provider switch
    {
        ModelProvider.Local => SelectedLocalModel?.Name ?? "no model selected",
        ModelProvider.SelfHosted => string.IsNullOrWhiteSpace(SelfHostedModelId) ? "no model id" : SelfHostedModelId,
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

        try
        {
            var resolution = await ResolveEndpointAsync(ctx, entry, ct).ConfigureAwait(false);

            try
            {
                return await StreamOnceAsync(ctx, entry, resolution.Endpoint, userContent, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (resolution.Plan is { IsSplit: true } && IsTransportFailure(ex))
            {
                // A split pipeline died mid request, which with llama.cpp rpc takes the whole
                // coordinator down. Probe the sources that were engaged, plan again against
                // whatever still covers each section, and re-send the request once.
                entry.Flush();
                entry.Detail = "a source dropped, planning again";
                StatusMessage = "A source dropped, planning again";
                ctx.Feed.Info(
                    "Distributed run interrupted",
                    $"{Title} lost its pipeline: {ex.Message} Planning again with the sources still covering each section.");

                var replanned = await ReplanAfterFailureAsync(ctx, entry, resolution.Plan, ct).ConfigureAwait(false);

                entry.Append($"{Environment.NewLine}{Environment.NewLine}");
                return await StreamOnceAsync(ctx, entry, replanned.Endpoint, userContent, ct).ConfigureAwait(false);
            }
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
        ["selfHostedModelId"] = SelfHostedModelId,
        ["systemPrompt"] = SystemPrompt,
        ["temperature"] = Temperature,
        ["maxTokens"] = MaxTokens,
        ["contextSize"] = ContextSize,
        ["gpuLayers"] = GpuLayers,
        ["forceSplit"] = ForceSplit,
        ["splitProportions"] = SplitProportions,
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
        SelfHostedModelId = settings["selfHostedModelId"]?.GetValue<string>() ?? string.Empty;
        SystemPrompt = settings["systemPrompt"]?.GetValue<string>() ?? DefaultSystemPrompt;
        Temperature = settings["temperature"]?.GetValue<double>() ?? 0.4d;
        MaxTokens = settings["maxTokens"]?.GetValue<int>() ?? 4096;
        ContextSize = settings["contextSize"]?.GetValue<int>() ?? LlamaLaunchOptions.DefaultContextSize;
        GpuLayers = settings["gpuLayers"]?.GetValue<int>() ?? LlamaLaunchOptions.DefaultGpuLayers;
        ForceSplit = settings["forceSplit"]?.GetValue<bool>() ?? false;
        SplitProportions = settings["splitProportions"]?.GetValue<string>() ?? string.Empty;
        BaseUrl = settings["baseUrl"]?.GetValue<string>() ?? DefaultBaseUrlFor(Provider);
        ApiKey = settings["apiKey"]?.GetValue<string>() ?? string.Empty;
    }

    /// <summary>The base URL filled in when a provider is selected.</summary>
    public static string DefaultBaseUrlFor(ModelProvider provider) => provider switch
    {
        ModelProvider.OpenRouter => OpenRouterBaseUrl,
        _ => string.Empty
    };

    private async Task<NodeResult> StreamOnceAsync(
        NodeExecutionContext ctx,
        ActivityEvent entry,
        ModelEndpoint endpoint,
        string userContent,
        CancellationToken ct)
    {
        var onToken = new DelegateProgress<string>(entry.Append);

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

    private async Task<EndpointResolution> ResolveEndpointAsync(
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

            var openRouterUrl = string.IsNullOrWhiteSpace(BaseUrl) ? OpenRouterBaseUrl : BaseUrl;
            return new EndpointResolution(new ModelEndpoint(openRouterUrl, OpenRouterModel, ApiKey), null);
        }

        if (Provider == ModelProvider.SelfHosted)
        {
            if (string.IsNullOrWhiteSpace(BaseUrl))
            {
                throw new InvalidOperationException($"{Title} has no base URL set for its self hosted server.");
            }

            if (string.IsNullOrWhiteSpace(SelfHostedModelId))
            {
                throw new InvalidOperationException($"{Title} has no model id set for its self hosted server.");
            }

            var key = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey;
            return new EndpointResolution(new ModelEndpoint(BaseUrl, SelfHostedModelId, key), null);
        }

        var modelPath = SelectedLocalModel?.Path;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new InvalidOperationException(
                $"{Title} has no local model selected. Drop a GGUF file into the models folder or add a folder from the settings panel.");
        }

        // The original escape hatch, unchanged: an explicit base URL on a local node means the
        // user is pointing at their own server, so nothing is spawned.
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new EndpointResolution(new ModelEndpoint(BaseUrl, Path.GetFileNameWithoutExtension(modelPath)), null);
        }

        return await ResolveManagedAsync(ctx, entry, modelPath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The managed local path: decide whether the model runs on this machine alone or split
    /// across sources, gate on coverage, and start or reuse the server the plan calls for.
    /// </summary>
    private async Task<EndpointResolution> ResolveManagedAsync(
        NodeExecutionContext ctx,
        ActivityEvent entry,
        string modelPath,
        CancellationToken ct)
    {
        CoveragePlan? plan = null;

        var metadata = TryReadMetadata(modelPath, out var metadataError);
        if (metadata is null)
        {
            // Without layer count and size there is nothing to plan with. A plain local
            // launch still works exactly as it always has; only splitting needs the header.
            if (ForceSplit)
            {
                throw new InvalidOperationException($"{Title} cannot plan a split: {metadataError}");
            }
        }
        else
        {
            await ProbeUnknownSourcesAsync(ctx, ct).ConfigureAwait(false);

            plan = ctx.Services.Coverage.Plan(metadata, ForceSplit, ParseSplitProportions(SplitProportions));
            if (!plan.IsComplete)
            {
                throw new InvalidOperationException($"{Title} cannot run: {plan.IncompleteReason}");
            }

            if (plan.IsSplit)
            {
                // Automatic but visible: the system chose the assembly, so it shows its work.
                ctx.Feed.Info("Coverage plan", $"{Title}: {plan.Summary}");
            }
        }

        var launchOptions = new LlamaLaunchOptions
        {
            ContextSize = ContextSize,
            GpuLayers = GpuLayers,
            RpcEndpoints = plan is { IsSplit: true } ? plan.RpcEndpoints : Array.Empty<string>(),
            TensorSplit = plan is { IsSplit: true } ? plan.TensorSplit : Array.Empty<double>()
        };

        var status = new DelegateProgress<string>(message =>
        {
            entry.Detail = message;
            StatusMessage = message;
        });

        var managedBaseUrl = await ctx.Services.LlamaServers
            .EnsureServerAsync(modelPath, launchOptions, status, ct)
            .ConfigureAwait(false);

        return new EndpointResolution(
            new ModelEndpoint(managedBaseUrl, Path.GetFileNameWithoutExtension(modelPath)),
            plan);
    }

    /// <summary>
    /// After a split pipeline failed: probe the sources that were engaged so the registry
    /// reflects reality, then resolve from scratch. The planner assigns whatever still covers
    /// each section; no source is special.
    /// </summary>
    private async Task<EndpointResolution> ReplanAfterFailureAsync(
        NodeExecutionContext ctx,
        ActivityEvent entry,
        CoveragePlan failedPlan,
        CancellationToken ct)
    {
        var engaged = failedPlan.Assignments
            .Select(a => a.Source)
            .OfType<InferenceSource>()
            .Where(s => !s.IsThisMachine)
            .Distinct()
            .ToList();

        if (engaged.Count > 0)
        {
            await Task.WhenAll(engaged.Select(s => ctx.Services.HealthMonitor.ProbeNowAsync(s, ct))).ConfigureAwait(false);
        }

        var modelPath = SelectedLocalModel?.Path;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new InvalidOperationException($"{Title} no longer has a local model selected.");
        }

        return await ResolveManagedAsync(ctx, entry, modelPath, ct).ConfigureAwait(false);
    }

    private static async Task ProbeUnknownSourcesAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var unknown = ctx.Services.Sources.RemoteSources
            .Where(s => s.State == SourceState.Unknown)
            .ToList();

        if (unknown.Count > 0)
        {
            await Task.WhenAll(unknown.Select(s => ctx.Services.HealthMonitor.ProbeNowAsync(s, ct))).ConfigureAwait(false);
        }
    }

    private static GgufModelInfo? TryReadMetadata(string modelPath, out string error)
    {
        try
        {
            error = string.Empty;
            return GgufMetadata.Read(modelPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }

    private static IReadOnlyList<double>? ParseSplitProportions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new List<double>(parts.Length);

        foreach (var part in parts)
        {
            if (!double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
                || value <= 0)
            {
                return null;
            }

            values.Add(value);
        }

        return values;
    }

    private static bool IsTransportFailure(Exception ex)
        => ex is ModelClientException or HttpRequestException or IOException or SocketException;

    partial void OnProviderChanged(ModelProvider value) => BaseUrl = DefaultBaseUrlFor(value);

    /// <summary>What resolution produced: the endpoint to call, and the coverage plan behind it when the launch was distributed.</summary>
    private sealed record EndpointResolution(ModelEndpoint Endpoint, CoveragePlan? Plan);
}
