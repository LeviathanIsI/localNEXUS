namespace LocalNEXUS.App.Models.Extensions;

/// <summary>
/// A capability an extension implements, which is what decides how the host talks to it.
/// </summary>
/// <remarks>
/// One extension may implement more than one. A worker that both exposes tools and contributes a
/// node is a single process speaking both, because the transport underneath is the same.
/// </remarks>
public enum ExtensionContract
{
    /// <summary>The extension is an MCP server and contributes tools a model can call.</summary>
    Mcp,

    /// <summary>The extension contributes node types the graph can execute.</summary>
    Node,

    /// <summary>
    /// The extension bridges a spec driven planning tool, and the window gains a tab for it.
    /// </summary>
    /// <remarks>
    /// The one contract that changes the shape of the window rather than what a graph can do. It is
    /// deliberately not a general "contributes a view" capability: a tab is a place with its own
    /// idea of what is in it, and a contract that let any extension declare one would be a promise
    /// to render anything anybody sent.
    /// </remarks>
    Spec
}
