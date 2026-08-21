using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Holds the place of a node whose extension is not installed here.
/// </summary>
/// <remarks>
/// A graph is a file somebody keeps. Opening one on a machine that is missing an extension used
/// to be impossible to survive: the factory refused the unknown type key, the node was skipped,
/// its wires went with it, and the next save wrote a graph with a hole in it. The person who
/// installed the extension afterwards would find their work already gone.
/// <para>
/// So the node is kept rather than dropped. Everything read from the file is held untouched, the
/// type key, the settings payload and the pins with their saved identities, and written back out
/// exactly as it came in. Installing the extension and reopening restores the graph as it was.
/// This is the same rule that keeps every historical type key loading, applied to a type key that
/// belongs to somebody else.
/// </para>
/// <para>
/// It refuses to run, and says why. A placeholder that quietly produced nothing would let a run
/// report success having skipped a step.
/// </para>
/// </remarks>
public sealed class UnavailableNode : NodeBase
{
    private JsonObject _saved = new();

    public UnavailableNode(string typeKey)
        : base(typeKey)
    {
        TypeKey = typeKey;
    }

    /// <inheritdoc />
    public override string TypeKey { get; }

    /// <summary>
    /// Rebuilds the pins as the file recorded them, so the wires drawn to this node survive.
    /// </summary>
    /// <remarks>
    /// The pin type is not in the saved shape, and it does not need to be. Compatibility is
    /// decided when a wire is drawn, and these wires were drawn on a machine where the extension
    /// was present and the real types were known. Restoring them as Text keeps the pin, the name
    /// and the identity, which is all the connection needs to find its way back.
    /// </remarks>
    public void AdoptSavedPins(JsonArray? inputs, JsonArray? outputs)
    {
        Adopt(inputs, PinDirection.Input);
        Adopt(outputs, PinDirection.Output);
    }

    /// <inheritdoc />
    public override Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
        => throw new InvalidOperationException(
            $"'{TypeKey}' is contributed by an extension that is not installed for this project. " +
            "Install it from Settings, then open this graph again. The node and its wires have been kept.");

    /// <inheritdoc />
    public override JsonObject SaveSettings() => (JsonObject)_saved.DeepClone();

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings) => _saved = (JsonObject)settings.DeepClone();

    private void Adopt(JsonArray? saved, PinDirection direction)
    {
        if (saved is null)
        {
            return;
        }

        foreach (var entry in saved.OfType<JsonObject>())
        {
            var name = entry["name"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            _ = direction == PinDirection.Input
                ? AddInput(name, PinType.Text)
                : AddOutput(name, PinType.Text);
        }
    }
}
