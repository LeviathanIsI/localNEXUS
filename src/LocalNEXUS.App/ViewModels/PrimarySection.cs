namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// What the activity bar switches between: the primary view the window is showing.
/// </summary>
/// <remarks>
/// Settings is not one of these. It is a panel that opens over whichever section is showing and
/// closes back onto it, because it is about the application rather than about a thing being
/// worked on, and returning from it should land where the work was left.
/// </remarks>
public enum PrimarySection
{
    /// <summary>The canvas, its run outline, and the node inspector.</summary>
    Workspace,

    /// <summary>What the mesh can serve, and this machine's contribution to it.</summary>
    Network
}
