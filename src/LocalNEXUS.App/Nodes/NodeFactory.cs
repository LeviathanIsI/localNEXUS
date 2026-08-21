using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Creates nodes, both for the palette and for the loader.
/// </summary>
/// <remarks>
/// Node construction lives here rather than in the view model or the serializer because both of
/// them need it, and because model nodes need their services injected. Registering a new node
/// type is a single entry in <see cref="Descriptors"/>.
/// </remarks>
public sealed class NodeFactory
{
    private readonly ModelCatalog _catalog;
    private readonly MeshManager _mesh;
    private readonly IDialogService _dialogs;
    private readonly AppConfig _config;

    public NodeFactory(ModelCatalog catalog, MeshManager mesh, IDialogService dialogs, AppConfig config)
    {
        _catalog = catalog;
        _mesh = mesh;
        _dialogs = dialogs;
        _config = config;
    }

    /// <summary>A node type as offered by the palette.</summary>
    /// <param name="TypeKey">The discriminator written to the graph file.</param>
    /// <param name="DisplayName">Label shown in the palette.</param>
    /// <param name="Description">Tooltip explaining what the node does.</param>
    public readonly record struct NodeDescriptor(string TypeKey, string DisplayName, string Description);

    /// <summary>Every node type that can be added to a graph, in palette order.</summary>
    public static IReadOnlyList<NodeDescriptor> Descriptors { get; } = new[]
    {
        new NodeDescriptor("Prompt", "Prompt", "Sends on what you typed in the chat box."),
        new NodeDescriptor("Triage", "Triage", "Reads your project and decides which files to leave alone, edit, or write new."),
        new NodeDescriptor("Model", "Model", "Asks a model, local or hosted, and sends on its reply."),
        new NodeDescriptor("Patch", "Patch", "Applies a change to the code passing through it."),
        new NodeDescriptor("CompilerCheck", "Compiler check", "Compiles the code and asks the model to fix whatever does not build."),
        new NodeDescriptor("Output", "Output", "Writes the finished files into your project.")
    };

    /// <summary>Creates a node of the given type, started from the application wide defaults.</summary>
    /// <remarks>
    /// Every key a node has ever saved itself under is accepted here, and the current key is the
    /// one written back. That is the whole of the migration, and it is not optional: a key this
    /// does not recognise is reported as an unknown type and the node is dropped along with every
    /// wire attached to it, so a rename without this silently eats somebody's graph. It happened
    /// once already, when the palette offered <c>Compile</c> while the node saved itself as
    /// <c>CompileCheck</c>.
    ///
    /// The old names are: Input for Prompt, Plan for Triage, Transform for Patch, and both
    /// CompileCheck and Compile for Compiler check.
    /// </remarks>
    /// <exception cref="NotSupportedException">The type key is not one this build has ever used.</exception>
    public NodeBase Create(string typeKey) => typeKey switch
    {
        "Prompt" or "Input" => new PromptNode(),
        "Triage" or "Plan" => new TriageNode
        {
            MapCharacters = _config.DefaultMapCharacters,
            CandidateCharacters = _config.DefaultCandidateCharacters,
            EmittedCharacters = _config.DefaultEmittedCharacters,
            CandidateLimit = _config.DefaultCandidateLimit
        },
        "Model" => new ModelNode(_catalog, _mesh, _dialogs)
        {
            ApiKey = _config.CloudApiKey ?? string.Empty
        },
        "Patch" or "Transform" => new PatchNode(),
        "CompilerCheck" or "CompileCheck" or "Compile" => new CompilerCheckNode { RetryLimit = _config.DefaultRetryLimit },
        "Output" => new OutputNode(),
        _ => throw new NotSupportedException($"Unknown node type '{typeKey}'.")
    };

    /// <summary>Creates a node of the given type at a canvas position.</summary>
    public NodeBase Create(string typeKey, double x, double y)
    {
        var node = Create(typeKey);
        node.X = x;
        node.Y = y;
        return node;
    }
}
