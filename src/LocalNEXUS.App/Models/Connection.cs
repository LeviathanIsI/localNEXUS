using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Models;

/// <summary>
/// A directed wire from an output pin to an input pin.
/// </summary>
/// <remarks>
/// The identifier quartet is what gets persisted; the resolved <see cref="Source"/> and
/// <see cref="Target"/> pin references are what the canvas binds to for live anchor positions.
/// Both views of the same fact are kept in sync by the constructor.
/// </remarks>
public sealed partial class Connection : ObservableObject
{
    /// <summary>
    /// True when the run should stop here and show what is passing.
    /// </summary>
    /// <remarks>
    /// On the wire rather than on either node, because the thing worth looking at is the value in
    /// transit and a node that fans out to three places sends something different down each one.
    ///
    /// Toggling it has nothing to do with whether a run is in progress. A breakpoint set while a
    /// graph is idle is the ordinary case, and one set halfway through a run applies to whatever
    /// has not passed yet, which is what anybody debugging would expect.
    /// </remarks>
    [ObservableProperty]
    private bool _hasBreakpoint;

    public Connection(Pin source, Pin target)
    {
        if (source.Direction != PinDirection.Output)
        {
            throw new ArgumentException("The source of a connection must be an output pin.", nameof(source));
        }

        if (target.Direction != PinDirection.Input)
        {
            throw new ArgumentException("The target of a connection must be an input pin.", nameof(target));
        }

        Source = source;
        Target = target;

        SourceNodeId = source.Owner.Id;
        SourcePinId = source.Id;
        TargetNodeId = target.Owner.Id;
        TargetPinId = target.Id;
    }

    /// <summary>Identifier of the node that owns <see cref="Source"/>.</summary>
    public Guid SourceNodeId { get; }

    /// <summary>Identifier of the output pin the wire leaves from.</summary>
    public Guid SourcePinId { get; }

    /// <summary>Identifier of the node that owns <see cref="Target"/>.</summary>
    public Guid TargetNodeId { get; }

    /// <summary>Identifier of the input pin the wire arrives at.</summary>
    public Guid TargetPinId { get; }

    /// <summary>The resolved output pin. Bound by the canvas for its live anchor point.</summary>
    public Pin Source { get; }

    /// <summary>The resolved input pin. Bound by the canvas for its live anchor point.</summary>
    public Pin Target { get; }

    /// <summary>The value kind carried by this wire. Source and target always agree.</summary>
    public PinType PinType => Source.PinType;

    public override string ToString() => $"{Source} -> {Target}";
}
