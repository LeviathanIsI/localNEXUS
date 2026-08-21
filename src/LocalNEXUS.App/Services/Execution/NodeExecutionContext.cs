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

    /// <summary>This run's identity in the record, or null when nothing is recording.</summary>
    public string? RunId => _run.RunId;

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
    /// The nodes an output pin feeds, in no particular order.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="GetSourceNode"/>. A node that needs a capability none of its
    /// inputs has can look along its own output wire for one, which is how a planner borrows the
    /// model that is going to do the writing. These nodes have not run yet, and do not need to
    /// have: what is being borrowed is their configuration, not their result.
    /// </remarks>
    public IReadOnlyList<NodeBase> GetTargetNodes(Pin outputPin)
    {
        ArgumentNullException.ThrowIfNull(outputPin);

        return _run.Graph.Connections
            .Where(c => c.Source == outputPin)
            .Select(c => c.Target.Owner)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// The first node reachable from this one that offers a capability, following output wires.
    /// </summary>
    /// <remarks>
    /// Breadth first and depth limited, so a planner two nodes upstream of the coder still finds
    /// it and a graph wired in a loop cannot walk forever. Cycles are rejected before a run
    /// starts, so the visited set is belt and braces rather than a real defence.
    /// </remarks>
    public T? FindDownstream<T>(int maxDepth = 4)
        where T : class
    {
        var visited = new HashSet<NodeBase> { Node };
        var frontier = new List<NodeBase> { Node };

        for (var depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            var next = new List<NodeBase>();

            foreach (var node in frontier)
            {
                foreach (var pin in node.Outputs)
                {
                    foreach (var target in GetTargetNodes(pin))
                    {
                        if (!visited.Add(target))
                        {
                            continue;
                        }

                        if (target is T match)
                        {
                            return match;
                        }

                        next.Add(target);
                    }
                }
            }

            frontier = next;
        }

        return null;
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
