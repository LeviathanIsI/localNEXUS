using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Models.Extensions;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Extensions;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// A node type contributed by an extension. Running it is a call out to that extension's worker
/// process.
/// </summary>
/// <remarks>
/// This is an ordinary <see cref="NodeBase"/> and that is the entire point. <c>GraphExecutor</c>
/// sorts it, gathers its inputs from its wires and calls <see cref="ExecuteAsync"/> exactly as it
/// does for a node written here, and cannot tell the difference. Nothing was added to the
/// executor to make extension nodes work, and nothing should be: the moment it knows that some
/// nodes are special, every future node type has to be taught to it too.
/// <para>
/// The worker never sees the canvas, the executor, the graph or the project. It is given the
/// values on this node's input pins and its settings, and it returns values for the output pins.
/// That is the whole of its authority.
/// </para>
/// </remarks>
public sealed partial class ExtensionNode : NodeBase
{
    /// <summary>How long one call to the worker is given before it is treated as hung.</summary>
    [ObservableProperty]
    private int _timeoutSeconds = 120;

    private readonly ExtensionHost _host;
    private readonly ExtensionRegistry _registry;
    private readonly NodeContribution _contribution;

    private JsonObject _settings = new();

    public ExtensionNode(
        ExtensionHost host,
        ExtensionRegistry registry,
        InstalledExtension extension,
        NodeContribution contribution)
        : base(contribution.DisplayName)
    {
        _host = host;
        _registry = registry;
        _contribution = contribution;

        ExtensionId = extension.Manifest.Id;
        ExtensionName = extension.Manifest.Name;

        foreach (var pin in contribution.Inputs)
        {
            AddInput(pin.Name, pin.Type);
        }

        foreach (var pin in contribution.Outputs)
        {
            AddOutput(pin.Name, pin.Type);
        }
    }

    /// <summary>Which extension contributes this node.</summary>
    public string ExtensionId { get; }

    /// <summary>That extension's display name, for the inspector.</summary>
    public string ExtensionName { get; }

    /// <summary>What the extension says this node does.</summary>
    public string Description => _contribution.Description;

    /// <summary>The settings schema the extension declared, or null when it declared none.</summary>
    public JsonObject? SettingsSchema => _contribution.SettingsSchema;

    /// <summary>The node's settings, as a free form object shaped by the extension's own schema.</summary>
    public JsonObject Settings
    {
        get => _settings;
        set
        {
            _settings = value;
            OnPropertyChanged();
        }
    }

    /// <inheritdoc />
    public override string TypeKey => _contribution.TypeKey;

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var extension = _registry.Find(ExtensionId)
            ?? throw new InvalidOperationException(
                $"{Title} needs the extension '{ExtensionId}', which is not registered against this project.");

        if (!extension.IsEnabled)
        {
            throw new InvalidOperationException(
                $"{Title} needs the extension '{extension.Manifest.Name}', which is switched off.");
        }

        StatusMessage = "starting the extension";

        var session = await _host
            .EnsureRunningAsync(extension, ExtensionContract.Node, ct)
            .ConfigureAwait(false);

        var client = new NodeWorkerClient(session);

        var inputs = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pin in Inputs)
        {
            inputs[pin.Name] = ctx.GetText(pin) ?? string.Empty;
        }

        StatusMessage = "running";

        var outputs = await client
            .ExecuteAsync(
                TypeKey,
                Id,
                inputs,
                Settings,
                TimeSpan.FromSeconds(Math.Max(1, TimeoutSeconds)),
                ct)
            .ConfigureAwait(false);

        var produced = new Dictionary<Guid, object?>();

        foreach (var pin in Outputs)
        {
            if (!outputs.TryGetValue(pin.Name, out var value))
            {
                // Named rather than silently empty: a worker that forgot an output pin is a bug
                // in that worker, and whoever is running it needs to be able to tell whose bug
                // it is without reading someone else's source.
                throw new InvalidOperationException(
                    $"{extension.Manifest.Name} ran '{TypeKey}' but returned nothing for the output pin '{pin.Name}'.");
            }

            produced[pin.Id] = value;
        }

        ctx.Feed.Add(
            ActivityKind.Info,
            $"{Title} ran in {extension.Manifest.Name}",
            $"{inputs.Count} input(s), {outputs.Count} output(s)",
            Id);

        StatusMessage = $"{outputs.Count} output(s)";
        return NodeResult.FromValues(produced);
    }

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["extensionId"] = ExtensionId,
        ["timeoutSeconds"] = TimeoutSeconds,
        ["settings"] = Settings.DeepClone()
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        TimeoutSeconds = settings["timeoutSeconds"]?.GetValue<int>() ?? 120;
        Settings = settings["settings"] as JsonObject is { } saved
            ? (JsonObject)saved.DeepClone()
            : new JsonObject();
    }
}
