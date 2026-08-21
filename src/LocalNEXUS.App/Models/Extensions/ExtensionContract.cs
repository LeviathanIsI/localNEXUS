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
    Node
}
