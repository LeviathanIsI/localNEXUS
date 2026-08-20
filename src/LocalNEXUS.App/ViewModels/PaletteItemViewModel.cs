using System.Windows.Input;

namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// One node type as a menu offers it: what it is called, what it does, and the command that adds
/// one.
/// </summary>
/// <remarks>
/// The command travels with the item rather than being reached for through the visual tree.
/// A menu that is offered from a context menu lives in its own popup, which is a separate visual
/// tree, so binding back to an ancestor from inside one is the kind of thing that works until the
/// menu is opened from somewhere new. Carrying the command is one field and never breaks.
/// </remarks>
/// <param name="TypeKey">The discriminator the factory creates from.</param>
/// <param name="DisplayName">What the menu calls it.</param>
/// <param name="Description">One line explaining what the node does.</param>
/// <param name="Command">Adds a node of this type.</param>
public sealed record PaletteItemViewModel(
    string TypeKey,
    string DisplayName,
    string Description,
    ICommand Command);
