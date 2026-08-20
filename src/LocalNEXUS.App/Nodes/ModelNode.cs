using System.IO;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Dialogs;
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
    [NotifyPropertyChangedFor(nameof(IsNetwork))]
    [NotifyPropertyChangedFor(nameof(IsSelfHosted))]
    [NotifyPropertyChangedFor(nameof(IsOpenRouter))]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    private ModelProvider _provider = ModelProvider.Local;

    /// <summary>The GGUF selected from the catalog, when the provider is local.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    [NotifyPropertyChangedFor(nameof(ModelSourceText))]
    [NotifyPropertyChangedFor(nameof(EffectiveLocalModelPath))]
    private LocalModelInfo? _selectedLocalModel;

    /// <summary>
    /// A GGUF chosen by browsing, which this node runs instead of its catalogue selection. Null
    /// when the node uses the dropdown.
    /// </summary>
    /// <remarks>
    /// Per node on purpose. The alternative on offer, adding the folder to the catalogue, is a
    /// global and persistent change for the sake of one node, which is the wrong size of action
    /// for a model that simply lives on another drive.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    [NotifyPropertyChangedFor(nameof(ModelSource))]
    [NotifyPropertyChangedFor(nameof(HasModelFile))]
    [NotifyPropertyChangedFor(nameof(IsModelFileMissing))]
    [NotifyPropertyChangedFor(nameof(ModelSourceText))]
    [NotifyPropertyChangedFor(nameof(EffectiveLocalModelPath))]
    [NotifyCanExecuteChangedFor(nameof(ClearModelFileCommand))]
    private string? _modelFilePath;

    /// <summary>The network served model this node uses, when the provider is network.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelDisplayName))]
    private NetworkServedModel? _selectedNetworkModel;

    /// <summary>
    /// The persisted network model identity when it could not be resolved at load time, kept
    /// so saving the graph again does not silently drop the choice.
    /// </summary>
    private string? _unresolvedNetworkModelKey;

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
    /// The endpoint root. Filled in automatically when the provider changes. Leaving it blank
    /// for a local model means "use servers this application starts"; setting it points the
    /// node at a server that is already running somewhere else and nothing is spawned.
    /// </summary>
    [ObservableProperty]
    private string _baseUrl = string.Empty;

    /// <summary>Bearer token. Sent to OpenRouter, and to a self hosted server when set.</summary>
    [ObservableProperty]
    private string _apiKey = string.Empty;

    private readonly IDialogService _dialogs;

    public ModelNode(ModelCatalog catalog, MeshManager mesh, IDialogService dialogs)
        : base("Model")
    {
        Catalog = catalog;
        Mesh = mesh;
        _dialogs = dialogs;

        Prompt = AddInput("Text", PinType.Text);
        Completion = AddOutput("Code", PinType.Code);

        // A fresh node is usable straight away when the machine already has a model.
        SelectedLocalModel = catalog.Models.FirstOrDefault();
    }

    /// <summary>The GGUF files available for the local provider.</summary>
    public ModelCatalog Catalog { get; }

    /// <summary>This install's mesh node: what the network serves, and where to send it.</summary>
    public MeshManager Mesh { get; }

    /// <summary>Receives the text to send to the model.</summary>
    public Pin Prompt { get; }

    /// <summary>Carries the model reply onwards.</summary>
    public Pin Completion { get; }

    /// <inheritdoc />
    public override string TypeKey => "Model";

    /// <summary>True when the local provider is selected. Drives which settings are shown.</summary>
    public bool IsLocal => Provider == ModelProvider.Local;

    /// <summary>True when the network provider is selected.</summary>
    public bool IsNetwork => Provider == ModelProvider.Network;

    /// <summary>True when the self hosted provider is selected.</summary>
    public bool IsSelfHosted => Provider == ModelProvider.SelfHosted;

    /// <summary>True when the OpenRouter provider is selected.</summary>
    public bool IsOpenRouter => Provider == ModelProvider.OpenRouter;

    /// <summary>Where this node's local GGUF comes from: the catalogue, or a file of its own.</summary>
    public LocalModelSource ModelSource
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ModelFilePath))
            {
                return LocalModelSource.Catalog;
            }

            return File.Exists(ModelFilePath) ? LocalModelSource.File : LocalModelSource.MissingFile;
        }
    }

    /// <summary>True while this node runs a file of its own rather than the catalogue selection.</summary>
    public bool HasModelFile => ModelSource is LocalModelSource.File or LocalModelSource.MissingFile;

    /// <summary>True when the chosen file is no longer on disk, which the panel says out loud.</summary>
    public bool IsModelFileMissing => ModelSource == LocalModelSource.MissingFile;

    /// <summary>The GGUF this node will actually run, whichever way it was chosen.</summary>
    public string? EffectiveLocalModelPath => HasModelFile ? ModelFilePath : SelectedLocalModel?.Path;

    /// <summary>
    /// Which of the two selections is in effect, so the panel is never ambiguous.
    /// </summary>
    /// <remarks>
    /// A file stays in effect until it is cleared, whatever happens in the dropdown above. The
    /// alternative, letting a catalogue selection silently drop the file, cannot be made to work
    /// consistently: re-choosing the entry that is already selected raises no change at all, so
    /// the rule would apply on some selections and not others.
    /// </remarks>
    public string ModelSourceText => ModelSource switch
    {
        LocalModelSource.File => "This node runs the file below, not the catalogue selection above.",
        LocalModelSource.MissingFile => "This node points at a file that is no longer there.",
        _ => SelectedLocalModel is null
            ? "No model selected. Choose one above, or browse for a file anywhere on disk."
            : "This node runs the catalogue selection above."
    };

    /// <summary>The model this node will use, for display on the canvas.</summary>
    public string ModelDisplayName => Provider switch
    {
        ModelProvider.Local => LocalModelName(EffectiveLocalModelPath) ?? "no model selected",
        ModelProvider.Network => SelectedNetworkModel?.DisplayLabel ?? "no network model",
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
            // Recovery from a source dropping mid request belongs to the engine now: the mesh
            // routes around peers it has retired, so a node that second guessed it here would
            // be racing the thing that actually knows the topology.
            var endpoint = await ResolveEndpointAsync(ctx, entry, ct).ConfigureAwait(false);
            return await StreamOnceAsync(ctx, entry, endpoint, userContent, ct).ConfigureAwait(false);
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
        ["localModelFilePath"] = ModelFilePath,
        ["networkModel"] = SelectedNetworkModel?.ModelKey ?? _unresolvedNetworkModelKey,
        ["openRouterModel"] = OpenRouterModel,
        ["selfHostedModelId"] = SelfHostedModelId,
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
        SelectedLocalModel = Catalog.FindByPath(localPath);

        var filePath = settings["localModelFilePath"]?.GetValue<string>();

        // Graphs saved before a node could hold its own file recorded one path either way. A
        // path that no longer resolves in the catalogue is exactly what the override describes,
        // so it is restored as one rather than dropped, missing file and all.
        if (string.IsNullOrWhiteSpace(filePath) && SelectedLocalModel is null && !string.IsNullOrWhiteSpace(localPath))
        {
            filePath = localPath;
        }

        ModelFilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;

        var networkKey = settings["networkModel"]?.GetValue<string>();
        SelectedNetworkModel = Mesh.FindByKey(networkKey);
        _unresolvedNetworkModelKey = SelectedNetworkModel is null ? networkKey : null;

        OpenRouterModel = settings["openRouterModel"]?.GetValue<string>() ?? string.Empty;
        SelfHostedModelId = settings["selfHostedModelId"]?.GetValue<string>() ?? string.Empty;
        SystemPrompt = settings["systemPrompt"]?.GetValue<string>() ?? DefaultSystemPrompt;
        Temperature = settings["temperature"]?.GetValue<double>() ?? 0.4d;
        MaxTokens = settings["maxTokens"]?.GetValue<int>() ?? 4096;
        ContextSize = settings["contextSize"]?.GetValue<int>() ?? LlamaLaunchOptions.DefaultContextSize;
        GpuLayers = settings["gpuLayers"]?.GetValue<int>() ?? LlamaLaunchOptions.DefaultGpuLayers;
        BaseUrl = settings["baseUrl"]?.GetValue<string>() ?? DefaultBaseUrlFor(Provider);
        ApiKey = settings["apiKey"]?.GetValue<string>() ?? string.Empty;
    }

    /// <summary>
    /// Picks a GGUF anywhere on disk for this node alone. The catalogue is left untouched, which
    /// is the point: nothing about another node's choices changes.
    /// </summary>
    [RelayCommand]
    private void BrowseForModelFile()
    {
        var current = EffectiveLocalModelPath;
        var startIn = string.IsNullOrWhiteSpace(current) ? AppPaths.Models : Path.GetDirectoryName(current);

        var picked = _dialogs.PickOpenFile(
            "Choose a GGUF model file for this node",
            "GGUF models (*.gguf)|*.gguf|All files (*.*)|*.*",
            startIn);

        if (!string.IsNullOrWhiteSpace(picked))
        {
            ModelFilePath = Path.GetFullPath(picked);
        }
    }

    /// <summary>Drops the file override so the node goes back to its catalogue selection.</summary>
    [RelayCommand(CanExecute = nameof(HasModelFile))]
    private void ClearModelFile() => ModelFilePath = null;

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

    /// <summary>
    /// Works out where this node's request goes. Local models are served by a process this
    /// application starts; network models are served by the mesh, which decides for itself
    /// whether that means one peer or layer stages across several.
    /// </summary>
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

            var openRouterUrl = string.IsNullOrWhiteSpace(BaseUrl) ? OpenRouterBaseUrl : BaseUrl;
            return new ModelEndpoint(openRouterUrl, OpenRouterModel, ApiKey);
        }

        if (Provider == ModelProvider.Network)
        {
            return ResolveNetwork(ctx);
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
            return new ModelEndpoint(BaseUrl, SelfHostedModelId, key);
        }

        if (ModelSource == LocalModelSource.MissingFile)
        {
            throw new InvalidOperationException(
                $"{Title} points at a model file that is no longer there: {ModelFilePath}. "
                + "Browse for it again, or clear the file to go back to the catalogue selection.");
        }

        var modelPath = EffectiveLocalModelPath;
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new InvalidOperationException(
                $"{Title} has no local model selected. Drop a GGUF file into the models folder, add a folder, or browse for a file from the settings panel.");
        }

        // The original escape hatch, unchanged: an explicit base URL on a local node means the
        // user is pointing at their own server, so nothing is spawned.
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new ModelEndpoint(BaseUrl, Path.GetFileNameWithoutExtension(modelPath));
        }

        var status = new DelegateProgress<string>(message =>
        {
            entry.Detail = message;
            StatusMessage = message;
        });

        var launchOptions = new LlamaLaunchOptions { ContextSize = ContextSize, GpuLayers = GpuLayers };

        var managedBaseUrl = await ctx.Services.LlamaServers
            .EnsureServerAsync(modelPath, launchOptions, status, ct)
            .ConfigureAwait(false);

        return new ModelEndpoint(managedBaseUrl, Path.GetFileNameWithoutExtension(modelPath));
    }

    /// <summary>
    /// Points the request at the mesh. The gate is the mesh's own answer to whether it can
    /// assemble this model right now, and a refusal repeats the reason it gave rather than
    /// inventing one.
    /// </summary>
    private ModelEndpoint ResolveNetwork(NodeExecutionContext ctx)
    {
        var mesh = ctx.Services.Mesh;

        var networkModel = SelectedNetworkModel
            ?? throw new InvalidOperationException(
                $"{Title} has no network model selected. Pick one in the Network tab or the node settings.");

        if (!mesh.IsRunning)
        {
            throw new InvalidOperationException(
                $"{Title} cannot use {networkModel.DisplayLabel}: this install's mesh node is not running. Start it from the Network tab.");
        }

        if (!networkModel.CanRun)
        {
            // A model still coming up and one the mesh cannot assemble are both refusals, but
            // they are not the same news, so the message says which it is.
            var detail = networkModel.StatusDetail ?? (networkModel.Availability == ModelAvailability.Blocked
                ? "the mesh cannot assemble it right now."
                : "the mesh is still bringing it up.");

            throw new InvalidOperationException(
                networkModel.Availability == ModelAvailability.Blocked
                    ? $"{Title} cannot use {networkModel.DisplayLabel}. {detail}"
                    : $"{Title} cannot use {networkModel.DisplayLabel} yet. {detail}");
        }

        // Automatic but visible: the mesh chose the assembly, so the run shows its work.
        if (networkModel.Plan is { IsSplit: true } plan)
        {
            ctx.Feed.Info("Coverage plan", $"{Title}: {plan.Summary}");
        }

        return new ModelEndpoint(mesh.ApiBaseUrl, networkModel.ModelId);
    }

    partial void OnProviderChanged(ModelProvider value) => BaseUrl = DefaultBaseUrlFor(value);

    private static string? LocalModelName(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : Path.GetFileNameWithoutExtension(path);
}
