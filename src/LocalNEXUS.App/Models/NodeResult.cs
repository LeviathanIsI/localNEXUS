namespace LocalNEXUS.App.Models;

/// <summary>
/// The values a node produced during one execution, keyed by the identifier of the
/// output pin they belong to.
/// </summary>
public sealed class NodeResult
{
    private static readonly Dictionary<Guid, object?> NoOutputs = new();

    private readonly Dictionary<Guid, object?> _outputs;

    private NodeResult(Dictionary<Guid, object?> outputs) => _outputs = outputs;

    /// <summary>A result carrying no values. Used by terminal nodes such as the output node.</summary>
    public static NodeResult Empty { get; } = new(NoOutputs);

    /// <summary>The produced values, keyed by output pin identifier.</summary>
    public IReadOnlyDictionary<Guid, object?> Outputs => _outputs;

    /// <summary>Creates a result that sets exactly one output pin.</summary>
    public static NodeResult FromPin(Pin pin, object? value)
    {
        if (pin.Direction != PinDirection.Output)
        {
            throw new ArgumentException("Only output pins can carry a node result.", nameof(pin));
        }

        return new NodeResult(new Dictionary<Guid, object?> { [pin.Id] = value });
    }

    /// <summary>Creates a result from an explicit map of output pin identifier to value.</summary>
    public static NodeResult FromValues(IEnumerable<KeyValuePair<Guid, object?>> values)
        => new(new Dictionary<Guid, object?>(values));
}
