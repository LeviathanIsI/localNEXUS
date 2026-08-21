using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Credentials;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.ProjectIndex;

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
        ICodeCompiler compiler,
        ProjectIndexService projectIndex,
        UnityProjectService unityProject,
        FileWriter fileWriter,
        IActivityFeed feed,
        ExtensionRegistry? extensions = null,
        ToolSupportProbe? toolSupport = null,
        ICredentialStore? credentials = null,
        RunCostTracker? cost = null)
    {
        Extensions = extensions;
        Credentials = credentials;
        Cost = cost ?? new RunCostTracker();
        ToolSupport = toolSupport ?? new ToolSupportProbe(new System.Net.Http.HttpClient());
        ModelClient = modelClient;
        Runtimes = runtimes;
        Mesh = mesh;
        Compiler = compiler;
        ProjectIndex = projectIndex;
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

    /// <summary>Answers whether a piece of code compiles against the open Unity project.</summary>
    public ICodeCompiler Compiler { get; }

    /// <summary>What the open Unity project already contains, so a run is not written blind.</summary>
    public ProjectIndexService ProjectIndex { get; }

    /// <summary>The Unity project that output nodes write into.</summary>
    public UnityProjectService UnityProject { get; }

    /// <summary>Writes generated files to disk.</summary>
    public FileWriter FileWriter { get; }

    /// <summary>The extensions registered against the open project, or null when none is open.</summary>
    public ExtensionRegistry? Extensions { get; }

    /// <summary>Answers whether a model behind an endpoint can call tools.</summary>
    public ToolSupportProbe ToolSupport { get; }

    /// <summary>The API keys for hosted providers, or null when nothing configured one.</summary>
    public ICredentialStore? Credentials { get; }

    /// <summary>What this run has spent so far.</summary>
    public RunCostTracker Cost { get; }

    /// <summary>What a run may cost before it asks first. Zero switches the warning off.</summary>
    public decimal CostWarningThreshold { get; init; }

    /// <summary>The live transcript of the run.</summary>
    public IActivityFeed Feed { get; }
}
