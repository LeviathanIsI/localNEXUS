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
        LlamaServerManager llamaServers,
        SourceRegistry sources,
        CoveragePlanner coverage,
        SourceHealthMonitor healthMonitor,
        UnityProjectService unityProject,
        FileWriter fileWriter,
        IActivityFeed feed)
    {
        ModelClient = modelClient;
        LlamaServers = llamaServers;
        Sources = sources;
        Coverage = coverage;
        HealthMonitor = healthMonitor;
        UnityProject = unityProject;
        FileWriter = fileWriter;
        Feed = feed;
    }

    /// <summary>Sends chat requests to local and cloud endpoints alike.</summary>
    public IModelClient ModelClient { get; }

    /// <summary>Starts and reuses llama-server processes, local and distributed alike.</summary>
    public LlamaServerManager LlamaServers { get; }

    /// <summary>Every source this install knows about, this machine included.</summary>
    public SourceRegistry Sources { get; }

    /// <summary>Computes which sources fill which sections and gates distributed launches.</summary>
    public CoveragePlanner Coverage { get; }

    /// <summary>Probes sources on demand, used before a launch and after a failure.</summary>
    public SourceHealthMonitor HealthMonitor { get; }

    /// <summary>The Unity project that output nodes write into.</summary>
    public UnityProjectService UnityProject { get; }

    /// <summary>Writes generated files to disk.</summary>
    public FileWriter FileWriter { get; }

    /// <summary>The live transcript of the run.</summary>
    public IActivityFeed Feed { get; }
}
