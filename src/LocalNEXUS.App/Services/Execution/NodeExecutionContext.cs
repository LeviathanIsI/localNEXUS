using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// Everything one node needs while it executes: the values on its input pins, the request the
/// user typed, the feed to report into, and the shared services.
/// </summary>
public sealed class NodeExecutionContext
{
    private readonly RunContext _run;

    public NodeExecutionContext(NodeBase node, RunContext run, ExecutionServices services)
    {
        Node = node;
        _run = run;
        Services = services;
    }

    /// <summary>The node being executed.</summary>
    public NodeBase Node { get; }

    /// <summary>The request typed into the chat box before the run started.</summary>
    public string UserRequest => _run.UserRequest;

    /// <summary>The live transcript of the run.</summary>
    public IActivityFeed Feed => Services.Feed;

    /// <summary>The services available to nodes.</summary>
    public ExecutionServices Services { get; }

    /// <summary>
    /// Reads the value arriving on an input pin by following its incoming wire back to the
    /// upstream output pin. Returns null when the pin is unconnected or the upstream node
    /// produced nothing.
    /// </summary>
    public object? GetValue(Pin inputPin)
    {
        ArgumentNullException.ThrowIfNull(inputPin);

        var connection = _run.Graph.Connections.FirstOrDefault(c => c.Target == inputPin);
        if (connection is null)
        {
            return null;
        }

        return _run.TryGetValue(connection.Source, out var value) ? value : null;
    }

    /// <summary>Reads an input pin as text, yielding an empty string when nothing arrived.</summary>
    public string GetText(Pin inputPin) => GetValue(inputPin)?.ToString() ?? string.Empty;

    /// <summary>True when the given input pin has an incoming wire.</summary>
    public bool IsConnected(Pin inputPin) => _run.Graph.Connections.Any(c => c.Target == inputPin);

    /// <summary>
    /// The node on the other end of an input pin's wire, or null when the pin is unconnected.
    /// </summary>
    /// <remarks>
    /// A node that needs to ask its upstream neighbour for something, rather than merely read
    /// what it produced, needs to be able to find it. Following the wire is the graph's own
    /// answer to who that is, so nothing here has to know what kind of node either end is.
    /// </remarks>
    public NodeBase? GetSourceNode(Pin inputPin)
    {
        ArgumentNullException.ThrowIfNull(inputPin);

        var connection = _run.Graph.Connections.FirstOrDefault(c => c.Target == inputPin);
        return connection?.Source.Owner;
    }

    /// <summary>
    /// A context belonging to another node of the same run, so that node can read its own inputs.
    /// </summary>
    /// <remarks>
    /// Used when one node asks another to do more work. The run, its values and its services are
    /// shared; only the node the context is about differs.
    /// </remarks>
    public NodeExecutionContext ForNode(NodeBase node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return ReferenceEquals(node, Node) ? this : new NodeExecutionContext(node, _run, Services);
    }
}
