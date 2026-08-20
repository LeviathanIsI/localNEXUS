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
        new NodeDescriptor("Input", "Input", "Emits the request typed into the chat box."),
        new NodeDescriptor("Plan", "Plan", "Reads the open Unity project and works out which files the request needs."),
        new NodeDescriptor("Model", "Model", "Sends its input to a local or hosted model and emits the reply."),
        new NodeDescriptor("Transform", "Transform", "Rewrites the value passing through it with a template or a C# expression."),
        new NodeDescriptor("CompileCheck", "Compile check", "Compiles the code passing through it and asks the model that wrote it to fix what does not."),
        new NodeDescriptor("Output", "Output", "Writes its input to a file inside the opened Unity project.")
    };

    /// <summary>Creates a node of the given type, started from the application wide defaults.</summary>
    /// <remarks>
    /// The key a node is created from has to be the key it saves itself under, or a graph does not
    /// survive being reopened. <c>Compile</c> is still accepted because the palette used to offer
    /// that key while the node saved itself as <c>CompileCheck</c>, and a graph written by that
    /// build is worth still being able to open.
    /// </remarks>
    /// <exception cref="NotSupportedException">The type key is not one this build knows about.</exception>
    public NodeBase Create(string typeKey) => typeKey switch
    {
        "Input" => new InputNode(),
        "Plan" => new PlanNode
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
        "Transform" => new TransformNode(),
        "CompileCheck" or "Compile" => new CompileCheckNode { RetryLimit = _config.DefaultRetryLimit },
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
