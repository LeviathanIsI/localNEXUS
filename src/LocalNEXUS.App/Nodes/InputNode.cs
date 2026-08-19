using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// The entry point of a graph. Emits the text typed into the chat box.
/// </summary>
/// <remarks>
/// The node reads the request from the execution context rather than having the executor push a
/// value into it. That keeps the executor a plain graph walker with no knowledge of node types.
/// </remarks>
public sealed class InputNode : NodeBase
{
    public InputNode()
        : base("Request")
    {
        Request = AddOutput("Text", PinType.Text);
    }

    /// <summary>Carries the user request into the graph.</summary>
    public Pin Request { get; }

    /// <inheritdoc />
    public override string TypeKey => "Input";

    /// <inheritdoc />
    public override Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var request = ctx.UserRequest;
        StatusMessage = string.IsNullOrWhiteSpace(request)
            ? "Empty request"
            : $"{request.Length} characters";

        return Task.FromResult(NodeResult.FromPin(Request, request));
    }

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new();

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        // The input node has no settings; its value comes from the chat box at run time.
    }
}
