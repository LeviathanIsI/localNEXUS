using System.Text.Json.Nodes;
using LocalNEXUS.App.Models.Extensions;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Speaks the node contract to a worker process.
/// </summary>
/// <remarks>
/// The wire protocol, stated once here because it is the thing an extension author has to
/// implement:
/// <list type="bullet">
/// <item><c>node/describe</c>, host to worker, no parameters. Returns the node types the worker
/// actually implements. This exists so that test connect can catch a manifest that disagrees
/// with its own worker, which is otherwise a failure that only shows up mid run.</item>
/// <item><c>node/execute</c>, host to worker, with the type key, a run id, the gathered inputs
/// and the node's settings. Returns the outputs keyed by pin name.</item>
/// <item><c>node/cancel</c>, host to worker, a notification carrying the run id.</item>
/// <item><c>node/progress</c>, worker to host, a notification. Its text becomes the node's
/// status line, so a worker that reports "2 of 5" gets the progress bar the built in nodes
/// get, for free and without knowing that is what it is doing.</item>
/// <item><c>node/log</c>, worker to host, a notification that reaches the activity feed.</item>
/// </list>
/// Newline delimited JSON-RPC 2.0 over stdio, the same framing as MCP. One transport for both
/// contracts was worth more than any efficiency a bespoke protocol might have bought.
/// </remarks>
public sealed class NodeWorkerClient
{
    private static readonly TimeSpan DescribeTimeout = TimeSpan.FromSeconds(30);

    private readonly JsonRpcConnection _connection;

    public NodeWorkerClient(ExtensionSession session)
        => _connection = session.Rpc
            ?? throw new ExtensionException(
                "This session is not a node contract session, so the node protocol cannot be spoken over it.");

    /// <summary>
    /// Asks the worker which node types it implements.
    /// </summary>
    public async Task<IReadOnlyList<NodeContribution>> DescribeAsync(CancellationToken ct)
    {
        var result = await _connection
            .InvokeAsync("node/describe", null, DescribeTimeout, ct)
            .ConfigureAwait(false);

        if (result is not JsonObject payload || payload["nodes"] is not JsonArray nodes)
        {
            throw new ExtensionException(
                "The worker answered node/describe without a 'nodes' array, so what it contributes could not be read.");
        }

        var described = new List<NodeContribution>();

        foreach (var entry in nodes.OfType<JsonObject>())
        {
            described.Add(ReadNode(entry));
        }

        return described;
    }

    /// <summary>
    /// Runs one node in the worker and returns its outputs.
    /// </summary>
    /// <exception cref="ExtensionException">The worker refused, failed, or did not answer.</exception>
    public async Task<IReadOnlyDictionary<string, string>> ExecuteAsync(
        string typeKey,
        Guid runId,
        IReadOnlyDictionary<string, string> inputs,
        JsonObject settings,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var inputObject = new JsonObject();

        foreach (var pair in inputs)
        {
            inputObject[pair.Key] = pair.Value;
        }

        var parameters = new JsonObject
        {
            ["typeKey"] = typeKey,
            ["runId"] = runId.ToString(),
            ["inputs"] = inputObject,
            ["settings"] = settings.DeepClone()
        };

        try
        {
            var result = await _connection
                .InvokeAsync("node/execute", parameters, timeout, ct)
                .ConfigureAwait(false);

            if (result is not JsonObject payload)
            {
                throw new ExtensionException(
                    $"The worker answered node/execute for '{typeKey}' with something that was not an object.");
            }

            var outputs = new Dictionary<string, string>(StringComparer.Ordinal);

            if (payload["outputs"] is JsonObject outputObject)
            {
                foreach (var pair in outputObject)
                {
                    outputs[pair.Key] = pair.Value?.GetValueKind() switch
                    {
                        null => string.Empty,
                        System.Text.Json.JsonValueKind.String => pair.Value!.GetValue<string>(),
                        _ => pair.Value!.ToJsonString()
                    };
                }
            }

            return outputs;
        }
        catch (OperationCanceledException)
        {
            // Tell the worker to stop rather than leaving it computing something nobody wants.
            await _connection
                .NotifyAsync("node/cancel", new JsonObject { ["runId"] = runId.ToString() }, CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
    }

    private static NodeContribution ReadNode(JsonObject entry)
    {
        var typeKey = entry["typeKey"]?.GetValue<string>()
            ?? throw new ExtensionException("A node in node/describe has no typeKey.");

        return new NodeContribution(
            typeKey,
            entry["displayName"]?.GetValue<string>() ?? typeKey,
            entry["description"]?.GetValue<string>() ?? string.Empty,
            ReadPins(entry["inputs"] as JsonArray, typeKey),
            ReadPins(entry["outputs"] as JsonArray, typeKey),
            entry["settingsSchema"] as JsonObject);
    }

    private static IReadOnlyList<PinContribution> ReadPins(JsonArray? pins, string typeKey)
    {
        if (pins is null)
        {
            return Array.Empty<PinContribution>();
        }

        var read = new List<PinContribution>();

        foreach (var pin in pins.OfType<JsonObject>())
        {
            var name = pin["name"]?.GetValue<string>()
                ?? throw new ExtensionException($"A pin on '{typeKey}' has no name.");

            read.Add(new PinContribution(name, ExtensionPinTypes.Parse(pin["type"]?.GetValue<string>(), typeKey, name)));
        }

        return read;
    }
}
