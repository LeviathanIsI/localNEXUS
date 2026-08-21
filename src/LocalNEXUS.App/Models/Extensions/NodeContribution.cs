using System.Text.Json.Nodes;

namespace LocalNEXUS.App.Models.Extensions;

/// <summary>One pin an extension node declares.</summary>
/// <param name="Name">Pin label, and the key its value travels under on the wire.</param>
/// <param name="Type">Which existing pin type it carries.</param>
public sealed record PinContribution(string Name, PinType Type);

/// <summary>
/// A node type an extension adds to the graph.
/// </summary>
/// <param name="TypeKey">Discriminator written into saved graphs. Namespaced by the extension id.</param>
/// <param name="DisplayName">What the palette and the node header call it.</param>
/// <param name="Description">One line saying what it does for the person using it.</param>
/// <param name="Inputs">Input pins, in the order they are drawn.</param>
/// <param name="Outputs">Output pins, in the order they are drawn.</param>
/// <param name="SettingsSchema">JSON schema for the node's settings, or null when it has none.</param>
/// <remarks>
/// Pin types are drawn from the existing <see cref="PinType"/> values and an extension cannot
/// invent one. Two unrelated extensions that both declared a type called the same thing would be
/// treated as compatible on the strength of a matching string, which is exactly the scattered
/// special case that keeping one compatibility table exists to prevent. A manifest naming a pin
/// type that does not exist fails validation and the extension is marked failed saying so.
/// </remarks>
public sealed record NodeContribution(
    string TypeKey,
    string DisplayName,
    string Description,
    IReadOnlyList<PinContribution> Inputs,
    IReadOnlyList<PinContribution> Outputs,
    JsonObject? SettingsSchema = null);
