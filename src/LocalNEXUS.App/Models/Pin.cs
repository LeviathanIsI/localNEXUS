using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Models;

/// <summary>
/// A single connection point on a node. Pins carry both the domain information used by
/// the executor (type, direction, owner) and the small amount of view state that the
/// canvas needs in order to draw wires (<see cref="Anchor"/>, <see cref="IsConnected"/>).
/// </summary>
public sealed partial class Pin : ObservableObject
{
    /// <summary>The point on the canvas where wires attach. Written by the canvas, read by connections.</summary>
    [ObservableProperty]
    private Point _anchor;

    /// <summary>True when at least one connection currently uses this pin.</summary>
    [ObservableProperty]
    private bool _isConnected;

    public Pin(NodeBase owner, string name, PinType pinType, PinDirection direction)
    {
        Owner = owner;
        Name = name;
        PinType = pinType;
        Direction = direction;
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Stable identity for this pin. Assigned at construction and restored verbatim by
    /// <see cref="Services.Persistence.GraphSerializer"/> so that saved connections still resolve.
    /// </summary>
    public Guid Id { get; internal set; }

    /// <summary>Label shown next to the connector on the canvas.</summary>
    public string Name { get; }

    /// <summary>The value kind this pin carries. Drives both colour and connection validation.</summary>
    public PinType PinType { get; }

    /// <summary>Whether this pin consumes or produces a value.</summary>
    public PinDirection Direction { get; }

    /// <summary>The node this pin belongs to.</summary>
    public NodeBase Owner { get; }

    public override string ToString() => $"{Owner.Title}.{Name} ({PinType}, {Direction})";
}
