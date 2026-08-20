using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// The services a node may reach for while executing. Passing this one object keeps node
/// signatures stable as capabilities are added, and keeps nodes free of any knowledge of how
/// those services were constructed.
/// </summary>
public sealed class ExecutionServices
{
    public ExecutionServices(
        IModelClient modelClient,
        RuntimeResolver runtimes,
        MeshManager mesh,
        UnityProjectService unityProject,
        FileWriter fileWriter,
        IActivityFeed feed)
    {
        ModelClient = modelClient;
        Runtimes = runtimes;
        Mesh = mesh;
        UnityProject = unityProject;
        FileWriter = fileWriter;
        Feed = feed;
    }

    /// <summary>Sends chat requests to local and cloud endpoints alike.</summary>
    public IModelClient ModelClient { get; }

    /// <summary>
    /// Serves models this machine runs on its own, on whichever local runtime the model's format
    /// calls for. Which one that is never reaches the node asking for it.
    /// </summary>
    public RuntimeResolver Runtimes { get; }

    /// <summary>This install's mesh node: what the network can serve, and where to send it.</summary>
    public MeshManager Mesh { get; }

    /// <summary>The Unity project that output nodes write into.</summary>
    public UnityProjectService UnityProject { get; }

    /// <summary>Writes generated files to disk.</summary>
    public FileWriter FileWriter { get; }

    /// <summary>The live transcript of the run.</summary>
    public IActivityFeed Feed { get; }
}
