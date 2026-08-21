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
    Code,

    /// <summary>
    /// A configured model, handed to a node that needs one to think with.
    /// </summary>
    /// <remarks>
    /// A reference rather than a value. What travels here is the model node itself, so one model
    /// configured once can be handed to several consumers, and looking at the canvas answers which
    /// model does the planning instead of leaving it to be inferred from what happens to be wired
    /// downstream.
    ///
    /// Appended rather than inserted. Nothing serialises this enum by number today, but the habit
    /// is the point: a pin's saved identity is matched by name with a positional fallback, and
    /// anything that reorders pins hands one of them another's identity.
    /// </remarks>
    Model
}
