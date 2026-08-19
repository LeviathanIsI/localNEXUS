using System.IO;
using System.Windows;
using System.Windows.Threading;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.ViewModels;
using LocalNEXUS.App.Views;

namespace LocalNEXUS.App;

/// <summary>
/// The composition root. Every service is constructed here once and handed to whoever needs it,
/// which keeps the rest of the application free of service location and static state.
/// </summary>
public partial class App : Application
{
    private LlamaServerManager? _llamaServers;
    private OpenAiCompatibleClient? _modelClient;
    private SourceHealthMonitor? _healthMonitor;
    private RpcWorkerManager? _rpcWorker;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            Compose();
        }
        catch (Exception ex)
        {
            Report("Startup", ex);
            Shutdown(1);
        }
    }

    /// <summary>Builds the object graph and shows the window.</summary>
    private void Compose()
    {
        AppPaths.EnsureCreated();

        var config = AppConfig.LoadOrCreate();

        var catalog = new ModelCatalog(config);
        catalog.Refresh();

        var graph = new GraphModel();
        var feed = new ActivityFeed(Dispatcher);
        var dialogs = new WindowsDialogService();

        var unityProject = new UnityProjectService();
        RestoreLastProject(config, unityProject, feed);

        var sources = new SourceRegistry(config);
        var coverage = new CoveragePlanner(sources);
        var networkModels = new NetworkModelIndex(catalog, sources, coverage, Dispatcher);

        var factory = new NodeFactory(catalog, networkModels);
        var serializer = new GraphSerializer(factory);

        var healthMonitor = new SourceHealthMonitor(sources, feed);
        _healthMonitor = healthMonitor;
        healthMonitor.Start();

        var rpcWorker = new RpcWorkerManager(config, feed);
        _rpcWorker = rpcWorker;
        _ = rpcWorker.RestoreAsync();

        _llamaServers = new LlamaServerManager();
        _modelClient = new OpenAiCompatibleClient();

        var services = new ExecutionServices(
            _modelClient,
            _llamaServers,
            sources,
            coverage,
            healthMonitor,
            unityProject,
            new FileWriter(),
            feed);
        var executor = new GraphExecutor(services);

        var feedViewModel = new ActivityFeedViewModel(executor, graph, feed, Dispatcher);
        var catalogViewModel = new ModelCatalogViewModel(catalog, dialogs);
        var networkViewModel = new NetworkViewModel(networkModels, sources, rpcWorker, healthMonitor, feed);

        var mainViewModel = new MainViewModel(
            graph,
            factory,
            serializer,
            dialogs,
            feed,
            feedViewModel,
            catalogViewModel,
            networkViewModel,
            unityProject,
            config);

        ReportEnvironment(feed, catalog);

        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Child processes and background loops are ours to clean up. Nothing must outlive the window.
        _healthMonitor?.Dispose();
        _rpcWorker?.Dispose();
        _llamaServers?.Dispose();
        _modelClient?.Dispose();

        base.OnExit(e);
    }

    private static void RestoreLastProject(AppConfig config, UnityProjectService unityProject, ActivityFeed feed)
    {
        if (string.IsNullOrWhiteSpace(config.LastUnityProjectPath))
        {
            return;
        }

        try
        {
            unityProject.Open(config.LastUnityProjectPath);
        }
        catch (DirectoryNotFoundException)
        {
            feed.Info(
                "Previous Unity project not found",
                $"{config.LastUnityProjectPath} no longer exists. Open a project from the File menu.");
        }
    }

    /// <summary>
    /// Writes the two facts that decide whether a local run can work at all: whether the
    /// llama-server binary is present, and how many models were found.
    /// </summary>
    private static void ReportEnvironment(ActivityFeed feed, ModelCatalog catalog)
    {
        var executable = AppPaths.FindLlamaServerExecutable();

        if (executable is null)
        {
            feed.Info(
                "Local inference unavailable",
                $"{AppPaths.LlamaServerExecutableName} was not found. Place a llama.cpp build in vendor\\llama to run local models. OpenRouter nodes work without it.");
        }
        else
        {
            feed.Info("Local inference ready", executable);
        }

        feed.Info(
            "Model catalog",
            catalog.Models.Count == 0
                ? $"No GGUF files found. Drop one into {AppPaths.Models} or add a folder from a model node."
                : $"{catalog.Models.Count} GGUF file(s) available.");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report("Dispatcher", e.Exception);
        e.Handled = true;
    }

    private static void Report(string context, Exception exception)
    {
        var logPath = CrashLog.Write(context, exception);

        var message = logPath is null
            ? exception.ToString()
            : $"{exception.Message}{Environment.NewLine}{Environment.NewLine}Full detail was written to:{Environment.NewLine}{logPath}";

        MessageBox.Show(message, "LocalNEXUS hit an unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
