namespace LocalNEXUS.App.Models;

/// <summary>
/// The single place that decides whether two pins may be wired together. The canvas calls
/// this while a wire is being dragged so the drop can be refused visually, and
/// <see cref="GraphModel.TryConnect"/> calls it again before mutating the graph.
/// </summary>
public static class ConnectionValidator
{
    /// <summary>The outcome of a validation attempt.</summary>
    /// <param name="IsValid">True when the connection may be created.</param>
    /// <param name="Reason">A short human readable explanation, shown on the pending wire.</param>
    public readonly record struct ValidationResult(bool IsValid, string Reason)
    {
        public static ValidationResult Valid(string reason) => new(true, reason);

        public static ValidationResult Invalid(string reason) => new(false, reason);
    }

    /// <summary>
    /// Validates a candidate wire. Pins may be supplied in either order, so a user can drag
    /// from an input to an output as well as the other way around.
    /// </summary>
    public static ValidationResult Validate(GraphModel graph, Pin? first, Pin? second)
    {
        ArgumentNullException.ThrowIfNull(graph);

        if (first is null || second is null)
        {
            return ValidationResult.Invalid("No target");
        }

        if (first == second)
        {
            return ValidationResult.Invalid("A pin cannot connect to itself");
        }

        if (first.Direction == second.Direction)
        {
            return ValidationResult.Invalid("Connect an output to an input");
        }

        var source = first.Direction == PinDirection.Output ? first : second;
        var target = first.Direction == PinDirection.Input ? first : second;

        if (source.Owner == target.Owner)
        {
            return ValidationResult.Invalid("A node cannot connect to itself");
        }

        if (!PinTypeCompatibility.CanFlow(source.PinType, target.PinType))
        {
            return ValidationResult.Invalid(PinTypeCompatibility.DescribeRefusal(source.PinType, target.PinType));
        }

        if (graph.IsInputOccupied(target))
        {
            return ValidationResult.Invalid($"{target.Name} is already connected");
        }

        if (graph.Connections.Any(c => c.Source == source && c.Target == target))
        {
            return ValidationResult.Invalid("Already connected");
        }

        return ValidationResult.Valid(PinTypeCompatibility.DescribeFlow(source.PinType, target.PinType));
    }
}
