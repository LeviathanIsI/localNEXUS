namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Where a model node's local GGUF comes from.
/// </summary>
/// <remarks>
/// The catalogue is the home location and the default. Pointing a single node at a file
/// elsewhere on disk is an escape hatch for the ordinary case of models scattered across drives,
/// and it is deliberately per node: registering the folder would change what every other node
/// sees. The missing case is its own state rather than a silent fall back to the catalogue,
/// because a node that quietly runs a different model than the one that was chosen is worse than
/// one that refuses and says which file it cannot find.
/// </remarks>
public enum LocalModelSource
{
    /// <summary>The selection from the catalogue dropdown.</summary>
    Catalog,

    /// <summary>A specific file chosen for this node, which is present on disk.</summary>
    File,

    /// <summary>A specific file chosen for this node, which is no longer there.</summary>
    MissingFile
}
