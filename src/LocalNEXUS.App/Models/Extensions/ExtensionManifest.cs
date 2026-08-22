namespace LocalNEXUS.App.Models.Extensions;

/// <summary>
/// Everything an extension declares about itself: who it is, what it contributes, what it needs,
/// and how to start it.
/// </summary>
/// <remarks>
/// The contribution list is the point of this file rather than an afterthought. An extension host
/// that only knew how to start processes would be a launcher; knowing what each one adds is what
/// lets the panel say what installing something will actually do, before it is installed, and
/// what lets the graph offer an extension's nodes in the palette without starting it.
/// </remarks>
/// <param name="Id">Stable identity, reverse domain style, unique within a project.</param>
/// <param name="Name">What a person calls it.</param>
/// <param name="Version">Version string as the author writes it.</param>
/// <param name="Description">A sentence or two on what it is for.</param>
/// <param name="Author">Who wrote it.</param>
/// <param name="Homepage">Where to read more, when there is somewhere.</param>
/// <param name="Contracts">Which contracts it implements.</param>
/// <param name="Tools">Tools it contributes, when it implements the MCP contract.</param>
/// <param name="Nodes">Node types it contributes, when it implements the node contract.</param>
/// <param name="Prerequisites">What has to be true before it can run.</param>
/// <param name="Launch">How to start it.</param>
/// <param name="Deprecated">Set when the thing behind it has been retired upstream, with the reason.</param>
public sealed record ExtensionManifest(
    string Id,
    string Name,
    string Version,
    string Description,
    string? Author,
    string? Homepage,
    IReadOnlyList<ExtensionContract> Contracts,
    IReadOnlyList<ToolContribution> Tools,
    IReadOnlyList<NodeContribution> Nodes,
    IReadOnlyList<ExtensionPrerequisite> Prerequisites,
    ExtensionLaunch Launch,
    string? Deprecated = null)
{
    /// <summary>True when this extension exposes tools a model can call.</summary>
    public bool ProvidesTools => Contracts.Contains(ExtensionContract.Mcp);

    /// <summary>True when this extension adds node types to the graph.</summary>
    public bool ProvidesNodes => Contracts.Contains(ExtensionContract.Node);

    /// <summary>True when this extension brings a tab with it.</summary>
    public bool ProvidesTab => Contracts.Contains(ExtensionContract.Spec);

    /// <summary>What it contributes, as one line for the list.</summary>
    public string ContributionSummary
    {
        get
        {
            var parts = new List<string>();

            if (ProvidesTools)
            {
                parts.Add(Tools.Count > 0 ? $"{Tools.Count} tools" : "tools");
            }

            if (ProvidesNodes)
            {
                parts.Add(Nodes.Count == 1 ? "1 node" : $"{Nodes.Count} nodes");
            }

            if (ProvidesTab)
            {
                parts.Add("a tab");
            }

            return parts.Count == 0 ? "nothing yet" : string.Join(", ", parts);
        }
    }
}
