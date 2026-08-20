namespace LocalNEXUS.App.Models;

/// <summary>
/// The kind of value that travels along a pin. Connections are only permitted
/// between pins that share the same <see cref="PinType"/>.
/// </summary>
/// <remarks>
/// New value kinds are added here and given a colour in
/// <see cref="Infrastructure.Converters.PinTypeToBrushConverter"/>. Nothing else needs to change.
/// </remarks>
public enum PinType
{
    /// <summary>Free form natural language, such as the user request.</summary>
    Text,

    /// <summary>Source code produced or transformed by a node.</summary>
    Code
}
