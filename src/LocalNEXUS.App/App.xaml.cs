using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Dialogs;
using LocalNEXUS.App.Services.Distributed;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Inference;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.App.Services.Python;
using LocalNEXUS.App.Services.Theming;
using LocalNEXUS.App.ViewModels;
using LocalNEXUS.App.Views;

namespace LocalNEXUS.App;

/// <summary>
/// The composition root. Every service is constructed here once and handed to whoever needs it,
/// which keeps the rest of the application free of service location and static state.
/// </summary>
public partial class App : Application
{
    private ChildProcessGroup? _children;
    private LlamaServerManager? _llamaServers;
    private PythonRuntimeManager? _pythonRuntime;
    private OpenAiCompatibleClient? _modelClient;
    private MeshManager? _mesh;
    private CancellationTokenSource? _provisioning;
    private CancellationTokenSource? _indexing;
    private ViewModels.NetworkViewModel? _network;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Every way this process can end, not just the tidy one. An engine process holds GPU
        // memory, so one left behind by a crash quietly degrades every run after it.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

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

        // The theme is applied before anything is constructed, so the window is painted in the
        // right palette from its first frame rather than flashing the default one.
        var themes = new ThemeService(config, Resources);
        themes.ApplySaved();

        // Written on first run so there is something to edit rather than something to invent.
        ModelPathsFile.EnsureCreated();

        var catalog = new ModelCatalog(config);
        catalog.Refresh();

        var graph = new GraphModel();
        var feed = new ActivityFeed(Dispatcher);
        var dialogs = new WindowsDialogService();

        var unityProject = new UnityProjectService();
        RestoreLastProject(config, unityProject, feed);

        // Owns every engine process this session starts. Built before anything can start one,
        // and given the chance to deal with anything a previous session failed to clean up.
        var children = new ChildProcessGroup();
        _children = children;
        ReportAbandonedProcesses(children, feed);

        var mesh = new MeshManager(config, feed, Dispatcher, children);
        _mesh = mesh;

        var factory = new NodeFactory(catalog, mesh, dialogs, config);
        var serializer = new GraphSerializer(factory);

        // Restoring the node is deliberately not awaited: composition must not block on a
        // child process, and the Network tab shows the node coming up on its own.
        _ = mesh.RestoreAsync();

        // The Python runtime has an environment to build before it can serve anything, so the
        // provisioner comes first and the runtime is handed the same instance the panel watches.
        var pythonEnvironment = new PythonProvisioner(children, feed, Dispatcher);

        _llamaServers = new LlamaServerManager(children);
        _pythonRuntime = new PythonRuntimeManager(children, pythonEnvironment);

        // Order is the order runtimes are asked, and each answers for exactly one format, so
        // adding a third changes this line and nothing else.
        var runtimes = new RuntimeResolver(_llamaServers, _pythonRuntime);

        _modelClient = new OpenAiCompatibleClient();

        // Roslyn against the open project's own Unity references. The reference set is cached
        // behind this and rebuilt only when the project's compiled assemblies change.
        var compiler = new RoslynUnityCompiler(new UnityReferenceResolver());

        // What the open project already contains. Built on demand rather than at startup, so a
        // session that never runs a graph never reads a project it was not asked about.
        var projectIndex = new ProjectIndexService();

        var services = new ExecutionServices(
            _modelClient,
            runtimes,
            mesh,
            compiler,
            projectIndex,
            unityProject,
            new FileWriter(),
            feed);
        var executor = new GraphExecutor(services);

        var feedViewModel = new ActivityFeedViewModel(executor, graph, feed, Dispatcher);
        var catalogViewModel = new ModelCatalogViewModel(catalog, dialogs);
        var pythonViewModel = new PythonEnvironmentViewModel(pythonEnvironment, dialogs);
        var networkViewModel = new NetworkViewModel(mesh, catalog, config, feed, dialogs);
        _network = networkViewModel;

        // Reading the project again is the settings panel's business but the work belongs to the
        // application, so the panel is handed the same call the startup path uses rather than a
        // second one that could drift from it.
        var indexing = new CancellationTokenSource();
        _indexing = indexing;

        var settingsViewModel = new AppSettingsViewModel(
            config,
            themes,
            catalog,
            catalogViewModel,
            pythonViewModel,
            networkViewModel,
            projectIndex,
            dialogs,
            () => IndexProjectAsync(projectIndex, unityProject.ProjectPath, feed, indexing.Token));

        var mainViewModel = new MainViewModel(
            graph,
            factory,
            serializer,
            dialogs,
            feed,
            feedViewModel,
            catalogViewModel,
            pythonViewModel,
            networkViewModel,
            unityProject,
            projectIndex,
            themes,
            settingsViewModel,
            config,
            Dispatcher);

        ReportEnvironment(feed, catalog);

        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Show();

        // Deliberately not awaited. Building the Python environment is a download measured in
        // gigabytes, and the window has to be usable while it runs: GGUF models work throughout,
        // and the feed and the model panel show how far it has got.
        _provisioning = new CancellationTokenSource();
        _ = ProvisionPythonAsync(pythonEnvironment, _provisioning.Token);

        // The project index is read when a project is opened rather than when a graph is run, so
        // that what the application knows about the project is visible before anything depends on
        // it, and so the first run does not pay for it. Not awaited: reading a large project takes
        // long enough to notice and the window has to be usable throughout.
        _ = IndexProjectAsync(projectIndex, unityProject.ProjectPath, feed, indexing.Token);

        unityProject.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(UnityProjectService.ProjectPath))
            {
                return;
            }

            projectIndex.Forget();
            _ = IndexProjectAsync(projectIndex, unityProject.ProjectPath, feed, indexing.Token);
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    /// <summary>
    /// Releases everything this session owns. Called from every exit path, and safe to call more
    /// than once because more than one of those paths can run.
    /// </summary>
    private void Cleanup()
    {
        // Order matters: the managers stop their own work first, then the group confirms that
        // every process they started is actually gone and closes the job that guarantees it.
        _provisioning?.Cancel();
        _indexing?.Cancel();
        (MainWindow?.DataContext as MainViewModel)?.Dispose();
        _network?.Dispose();
        _mesh?.Dispose();
        _llamaServers?.Dispose();
        _pythonRuntime?.Dispose();
        _modelClient?.Dispose();
        _children?.Dispose();
    }

    /// <summary>
    /// Reads what the open Unity project contains, in the background.
    /// </summary>
    /// <remarks>
    /// Reported to the feed with its timing, because how long this takes is the thing that decides
    /// whether the approach is usable and it should not be a number only a developer can see.
    /// </remarks>
    private static async Task IndexProjectAsync(
        ProjectIndexService index,
        string? projectPath,
        ActivityFeed feed,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return;
        }

        try
        {
            await index.EnsureAsync(projectPath, null, ct).ConfigureAwait(false);

            feed.Info(
                "Project index",
                $"{index.StatusText} {index.ReparsedCount} file(s) had to be read again; the rest came from the cache.");
        }
        catch (OperationCanceledException)
        {
            // The application is closing, or another project was opened.
        }
        catch (Exception ex)
        {
            CrashLog.Write("ProjectIndex", ex);
            feed.Info("Project index unavailable", ex.Message);
        }
    }

    /// <summary>
    /// Builds the Python environment in the background on every launch. A run that finds it
    /// already built verifies it and returns, so the cost after the first launch is one import.
    /// </summary>
    private static async Task ProvisionPythonAsync(PythonProvisioner provisioner, CancellationToken ct)
    {
        try
        {
            await provisioner.EnsureAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The application is closing. The child process group stops whatever uv had running.
        }
        catch (Exception ex)
        {
            CrashLog.Write("PythonProvisioning", ex);
        }
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
                ? $"No models found. Drop one into {AppPaths.Models}, add a folder from a model node, or list a folder in {AppPaths.ModelPathsFile}."
                : $"{catalog.Models.Count} model(s) available.");

        ReportBundledFont(feed);

        feed.Info(
            TransformNode.CanCompileScripts ? "Script transforms ready" : "Script transforms unavailable",
            TransformNode.CanCompileScripts
                ? "A Transform node can compile a C# expression, which is what strips a markdown fence off a model reply."
                : "The script compiler could not be built in this build, so a Transform node can only use its template. "
                  + "Fence stripping, which the repair loop relies on, will not work.");
    }

    /// <summary>
    /// Says whether the bundled monospace face actually loaded.
    /// </summary>
    /// <remarks>
    /// A font that fails to resolve does not throw; WPF quietly falls back to the next name in the
    /// family list and everything keeps working while looking different on every machine, which is
    /// exactly what bundling one was meant to prevent. Asking the question out loud is the only way
    /// to know, and it is the same path resolution gotcha the vendored binaries have, so it is
    /// worth reporting from the published exe rather than assumed from a development run.
    /// </remarks>
    private static void ReportBundledFont(ActivityFeed feed)
    {
        const string Expected = "JetBrains Mono";

        try
        {
            var loaded = Fonts
                .GetFontFamilies(new Uri("pack://application:,,,/"), "./Assets/Fonts/")
                .SelectMany(f => f.FamilyNames.Values)
                .Any(name => string.Equals(name, Expected, StringComparison.OrdinalIgnoreCase));

            feed.Info(
                loaded ? "Bundled font loaded" : "Bundled font unavailable",
                loaded
                    ? $"{Expected} is rendering paths, identifiers and diagnostics."
                    : $"{Expected} did not resolve from this build, so the monospace fallback is being used instead.");
        }
        catch (Exception ex) when (ex is IOException or UriFormatException or NotSupportedException)
        {
            feed.Info("Bundled font unavailable", ex.Message);
        }
    }

    /// <summary>
    /// Reports engine processes a previous session left behind. They have already been stopped by
    /// the time this runs; saying so is what tells the user why the machine had memory in use.
    /// </summary>
    private static void ReportAbandonedProcesses(ChildProcessGroup children, ActivityFeed feed)
    {
        var stopped = children.TerminateAbandoned();

        if (stopped > 0)
        {
            feed.Info(
                "Cleaned up after a previous session",
                $"{stopped} engine process(es) were still running from a session that did not close properly. They were stopped so this one starts from a clean machine.");
        }

        if (!children.HasKernelBackstop)
        {
            feed.Info(
                "Process cleanup is degraded",
                "Windows refused a job object, so engine processes are stopped explicitly but would survive this application being killed outright.");
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report("Dispatcher", e.Exception);
        e.Handled = true;
    }

    /// <summary>
    /// A fault on a background thread ends the process whatever this handler does, so the only
    /// useful work here is recording it and stopping the children before it goes.
    /// </summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CrashLog.Write("Unhandled", exception);
        }

        Cleanup();
    }

    /// <summary>
    /// A faulted task nobody awaited. It is observed here so it cannot bring the process down on
    /// its own, and recorded so the fault is not simply lost.
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLog.Write("UnobservedTask", e.Exception);
        e.SetObserved();
    }

    /// <summary>The last point at which this process can still run code. Reached on paths that skip OnExit.</summary>
    private void OnProcessExit(object? sender, EventArgs e) => Cleanup();

    private static void Report(string context, Exception exception)
    {
        var logPath = CrashLog.Write(context, exception);

        var message = logPath is null
            ? exception.ToString()
            : $"{exception.Message}{Environment.NewLine}{Environment.NewLine}Full detail was written to:{Environment.NewLine}{logPath}";

        MessageBox.Show(message, "LocalNEXUS hit an unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
