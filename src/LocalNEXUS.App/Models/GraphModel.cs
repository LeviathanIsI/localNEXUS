using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Models;

/// <summary>
/// The document being edited: a set of nodes and the wires between them.
/// </summary>
/// <remarks>
/// This type owns the invariants of the graph. Connections may only be added through
/// <see cref="TryConnect"/> so that the validation rules cannot be bypassed, and removing a
/// node always removes the wires that referenced it.
/// </remarks>
public sealed partial class GraphModel : ObservableObject
{
    /// <summary>Every node currently on the canvas.</summary>
    public ObservableCollection<NodeBase> Nodes { get; } = new();

    /// <summary>Every wire currently on the canvas.</summary>
    public ObservableCollection<Connection> Connections { get; } = new();

    /// <summary>Adds a node to the graph.</summary>
    public void AddNode(NodeBase node)
    {
        ArgumentNullException.ThrowIfNull(node);
        Nodes.Add(node);
    }

    /// <summary>Removes a node and every wire attached to it.</summary>
    public void RemoveNode(NodeBase node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var attached = Connections.Where(c => c.Source.Owner == node || c.Target.Owner == node).ToList();
        foreach (var connection in attached)
        {
            RemoveConnection(connection);
        }

        Nodes.Remove(node);
    }

    /// <summary>
    /// Adds a wire if <see cref="ConnectionValidator"/> permits it. Returns the reason when it does not.
    /// </summary>
    public bool TryConnect(Pin source, Pin target, out string failureReason)
    {
        var result = ConnectionValidator.Validate(this, source, target);
        if (!result.IsValid)
        {
            failureReason = result.Reason;
            return false;
        }

        var connection = new Connection(source, target);
        Connections.Add(connection);
        source.IsConnected = true;
        target.IsConnected = true;
        failureReason = string.Empty;
        return true;
    }

    /// <summary>Removes a wire and clears the connected flag on pins that no longer carry one.</summary>
    public void RemoveConnection(Connection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (!Connections.Remove(connection))
        {
            return;
        }

        RefreshConnectedFlag(connection.Source);
        RefreshConnectedFlag(connection.Target);
    }

    /// <summary>Removes every wire attached to the given pin.</summary>
    public void DisconnectPin(Pin pin)
    {
        ArgumentNullException.ThrowIfNull(pin);

        var attached = Connections.Where(c => c.Source == pin || c.Target == pin).ToList();
        foreach (var connection in attached)
        {
            RemoveConnection(connection);
        }
    }

    /// <summary>Empties the graph.</summary>
    public void Clear()
    {
        Connections.Clear();
        Nodes.Clear();
    }

    /// <summary>Finds a node by identifier, or null when it is not part of this graph.</summary>
    public NodeBase? FindNode(Guid nodeId) => Nodes.FirstOrDefault(n => n.Id == nodeId);

    /// <summary>Returns every wire that terminates on the given node.</summary>
    public IEnumerable<Connection> IncomingConnections(NodeBase node)
        => Connections.Where(c => c.Target.Owner == node);

    /// <summary>Returns every wire that originates at the given node.</summary>
    public IEnumerable<Connection> OutgoingConnections(NodeBase node)
        => Connections.Where(c => c.Source.Owner == node);

    /// <summary>Returns true when the given input pin already has a wire attached.</summary>
    public bool IsInputOccupied(Pin pin) => Connections.Any(c => c.Target == pin);

    private void RefreshConnectedFlag(Pin pin)
        => pin.IsConnected = Connections.Any(c => c.Source == pin || c.Target == pin);
}
