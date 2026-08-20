namespace LocalNEXUS.App.ViewModels.Network;

/// <summary>
/// Who can see a model the network serves.
/// </summary>
/// <remarks>
/// Read from the posture of the mesh rather than stored per model, because that is where the
/// answer actually lives: a private mesh is joined by invitation, so everything in it is reachable
/// only by someone who was invited, and advertising the mesh publicly makes all of it public at
/// once. When the engine grows a per model share setting this becomes that, and nothing above it
/// changes.
/// </remarks>
public enum ModelSharing
{
    /// <summary>Listed to everyone in the mesh, because the mesh itself is public.</summary>
    Public,

    /// <summary>Reachable only from inside a private mesh, which is joined with an invite.</summary>
    InviteOnly
}
