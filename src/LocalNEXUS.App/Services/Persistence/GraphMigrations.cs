using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// Brings a graph saved by an older build up to what this one expects.
/// </summary>
/// <remarks>
/// One file, named for what it is, so that everything this application knows about old documents
/// is in a place somebody would look. The serializer stays general: it restores nodes, pins and
/// connections without knowing what any of them are for, and then this runs.
///
/// A migration is preferred to a fallback. Keeping the old behaviour alive beside the new one
/// means both have to keep working forever and a graph never actually moves. Upgrading the
/// document once, on load, means the old mechanism can be deleted and the graph now says plainly
/// what it used to leave implied.
///
/// The rule the v1.2 bug set is the one being obeyed here: a graph that opens must open whole. A
/// warning is not a substitute for a wire, because nobody rereads a warning and the wire is what
/// made the graph work.
/// </remarks>
public static class GraphMigrations
{
    /// <summary>How far a planning model was ever searched for, which is what this reproduces.</summary>
    private const int SearchDepth = 4;

    /// <summary>
    /// Applies every migration to a freshly loaded graph.
    /// </summary>
    /// <returns>What was changed, for the feed. Empty when the graph was already current.</returns>
    public static IReadOnlyList<string> Apply(GraphModel graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var notes = new List<string>();
        ConnectPlanningModels(graph, notes);
        return notes;
    }

    /// <summary>
    /// Wires a Triage node to the model it used to find by looking downstream.
    /// </summary>
    /// <remarks>
    /// Before the Model pin existed, a Triage node planned with whatever model was reachable along
    /// its own output wires, up to four nodes away. That was invisible on the canvas and is gone.
    /// A graph saved under it has a Triage node with nothing on its Model input and would now
    /// refuse to run, so the same search runs once here and the wire it implied is made real.
    ///
    /// Only when the input is unconnected, so a graph saved by this build or later is never
    /// touched. Only the first model found, which is what the old search returned.
    /// </remarks>
    private static void ConnectPlanningModels(GraphModel graph, List<string> notes)
    {
        foreach (var triage in graph.Nodes.OfType<TriageNode>())
        {
            if (graph.Connections.Any(c => c.Target == triage.Model))
            {
                continue;
            }

            if (FindModelDownstream(graph, triage) is not { } model)
            {
                continue;
            }

            graph.Connections.Add(new Connection(model.Self, triage.Model));

            notes.Add(
                $"{triage.Title} now says which model it plans with: {model.Title}. "
                + "It used to be whichever model was wired after it, which was true and invisible.");
        }
    }

    /// <summary>The first model node reachable along a node's output wires, as the old search found it.</summary>
    private static ModelNode? FindModelDownstream(GraphModel graph, NodeBase start)
    {
        var visited = new HashSet<NodeBase> { start };
        var frontier = new List<NodeBase> { start };

        for (var depth = 0; depth < SearchDepth && frontier.Count > 0; depth++)
        {
            var next = new List<NodeBase>();

            foreach (var node in frontier)
            {
                foreach (var pin in node.Outputs)
                {
                    foreach (var connection in graph.Connections.Where(c => c.Source == pin))
                    {
                        var target = connection.Target.Owner;

                        if (!visited.Add(target))
                        {
                            continue;
                        }

                        if (target is ModelNode model)
                        {
                            return model;
                        }

                        next.Add(target);
                    }
                }
            }

            frontier = next;
        }

        return null;
    }
}
