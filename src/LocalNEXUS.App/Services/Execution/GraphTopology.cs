using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// Orders the nodes of a graph so that every node runs after the nodes that feed it.
/// </summary>
/// <remarks>
/// Kahn's algorithm. Nodes are dequeued in the order they appear on the canvas, so a graph with
/// several independent branches produces the same order on every run, which makes the activity
/// feed reproducible.
/// </remarks>
public static class GraphTopology
{
    /// <summary>The result of ordering a graph.</summary>
    /// <param name="Ordered">Nodes in execution order. Empty when a cycle was found.</param>
    /// <param name="Cycle">The nodes that could not be ordered because they form or feed a cycle.</param>
    public readonly record struct SortResult(IReadOnlyList<NodeBase> Ordered, IReadOnlyList<NodeBase> Cycle)
    {
        /// <summary>True when every node could be ordered.</summary>
        public bool IsAcyclic => Cycle.Count == 0;
    }

    /// <summary>Produces an execution order for the graph, or reports the nodes involved in a cycle.</summary>
    public static SortResult Sort(GraphModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var nodes = graph.Nodes.ToList();

        // Several wires can join the same pair of nodes through different pins. Collapse them so
        // that one dependency is not counted twice.
        //
        // A model wire is not one of them. It carries a reference to a configured model rather
        // than a value that model produced, so the consumer needs the model to exist, not to have
        // run. Counting it as a dependency would make the ordinary planning graph a cycle and
        // refuse to run it: a triage node feeds its plan to a model, and that same model is the
        // one triage plans with, so the two wires point in opposite directions between the same
        // pair of nodes. They are not a loop, because only one of them is a flow.
        //
        // This reasons about a pin type, never a node type. It is the same table
        // PinTypeCompatibility consults, and the executor above it still knows nothing about what
        // any node is or does.
        var edges = graph.Connections
            .Where(c => c.Source.PinType != PinType.Model)
            .Select(c => (From: c.Source.Owner, To: c.Target.Owner))
            .Where(e => e.From != e.To)
            .Distinct()
            .ToList();

        var dependents = nodes.ToDictionary(n => n, _ => new List<NodeBase>());
        var indegree = nodes.ToDictionary(n => n, _ => 0);

        foreach (var (from, to) in edges)
        {
            if (!dependents.ContainsKey(from) || !indegree.ContainsKey(to))
            {
                continue;
            }

            dependents[from].Add(to);
            indegree[to]++;
        }

        var ready = new Queue<NodeBase>(nodes.Where(n => indegree[n] == 0));
        var ordered = new List<NodeBase>(nodes.Count);

        while (ready.Count > 0)
        {
            var node = ready.Dequeue();
            ordered.Add(node);

            foreach (var dependent in dependents[node])
            {
                if (--indegree[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        if (ordered.Count == nodes.Count)
        {
            return new SortResult(ordered, Array.Empty<NodeBase>());
        }

        var stuck = nodes.Where(n => indegree[n] > 0).ToList();
        return new SortResult(Array.Empty<NodeBase>(), stuck);
    }
}
