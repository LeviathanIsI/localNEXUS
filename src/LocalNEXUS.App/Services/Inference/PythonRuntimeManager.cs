using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.App.Services.Processes;
using LocalNEXUS.App.Services.Python;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Owns the Python child processes that serve safetensors models on this machine.
/// </summary>
/// <remarks>
/// The same shape as the llama.cpp runtime, because the problem is the same: one server per
/// model, started once and reused, started silently, and owned by the child process group so
/// none of them outlives the application. What it starts is the transformers command line's own
/// server, which already speaks the OpenAI compatible API this application talks; that is why
/// nothing on the request path changes to accommodate it.
///
/// Python is never loaded into this process. It is a child process with an environment of its
/// own, built and owned by the provisioner, which is what keeps a second runtime from being able
/// to disturb the first.
/// </remarks>
public sealed class PythonRuntimeManager : IModelRuntime, IDisposable
{
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(20);

    /// <summary>The transformers command line module, run inside the environment's interpreter.</summary>
    private const string ServeModule = "transformers.cli.transformers";

    private readonly object _sync = new();
    private readonly Dictionary<string, PythonServerInstance> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _health = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ChildProcessGroup _children;
    private readonly PythonProvisioner _provisioner;

    private bool _disposed;

    public PythonRuntimeManager(ChildProcessGroup children, PythonProvisioner provisioner)
    {
        _children = children;
        _provisioner = provisioner;
    }

    /// <inheritdoc />
    public string Name => "the Python runtime";

    /// <inheritdoc />
    public bool CanServe(ModelDescriptor model) => model.Format == ModelFormat.Safetensors;

    /// <inheritdoc />
    public async Task<RuntimeEndpoint> EnsureServingAsync(
        ModelDescriptor model,
        ModelRuntimeOptions options,
        IProgress<string>? status,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!Directory.Exists(model.Path))
        {
            throw new ModelClientException($"The model folder no longer exists: {model.Path}");
        }

        if (_provisioner.DescribeUnavailability() is { } unavailable)
        {
            throw new ModelClientException($"{model.DisplayName} cannot be run. {unavailable}");
        }

        var fullPath = Path.GetFullPath(model.Path);

        // Serialised per model so two nodes asking for the same one start a single server. The
        // launch options do not enter the key: nothing in them changes how this server loads a
        // model, so two nodes with different context sizes still share one process.
        var gate = GetGate(fullPath);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            PythonServerInstance? existing;
            lock (_sync)
            {
                _servers.TryGetValue(fullPath, out existing);
            }

            if (existing is not null)
            {
                if (existing.IsRunning)
                {
                    status?.Report($"Reusing the Python server on port {existing.Port}");
                    return new RuntimeEndpoint(existing.BaseUrl, existing.ModelPath);
                }

                existing.Dispose();
                lock (_sync)
                {
                    _servers.Remove(fullPath);
                }
            }

            var instance = StartServer(fullPath, model.DisplayName, _children, _provisioner.InterpreterPath);
            lock (_sync)
            {
                _servers[fullPath] = instance;
            }

            try
            {
                await WaitUntilHealthyAsync(instance, model.DisplayName, status, ct).ConfigureAwait(false);
            }
            catch
            {
                instance.Dispose();
                lock (_sync)
                {
                    _servers.Remove(fullPath);
                }

                throw;
            }

            // The server pins itself to the exact string it was given and refuses every other
            // id, so that string is what goes in the request rather than a friendly name.
            return new RuntimeEndpoint(instance.BaseUrl, instance.ModelPath);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void ShutdownAll()
    {
        lock (_sync)
        {
            foreach (var server in _servers.Values)
            {
                server.Dispose();
            }

            _servers.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ShutdownAll();

        lock (_sync)
        {
            foreach (var gate in _gates.Values)
            {
                gate.Dispose();
            }

            _gates.Clear();
        }

        _health.Dispose();
    }

    private SemaphoreSlim GetGate(string key)
    {
        lock (_sync)
        {
            if (!_gates.TryGetValue(key, out var gate))
            {
                gate = new SemaphoreSlim(1, 1);
                _gates[key] = gate;
            }

            return gate;
        }
    }

    private static PythonServerInstance StartServer(
        string modelPath,
        string displayName,
        ChildProcessGroup children,
        string interpreter)
    {
        if (!File.Exists(interpreter))
        {
            throw new ModelClientException(
                "The Python runtime's interpreter is missing. Repair the runtime from the Local model panel.");
        }

        var port = ReserveFreePort();
        var startInfo = new ProcessStartInfo
        {
            FileName = interpreter,
            WorkingDirectory = AppPaths.PythonRoot,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Redirected so that closing it is a request the server could act on, and so the
            // group has the same lever over it that it has over every other engine process.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(ServeModule);
        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add(modelPath);
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add("--log-level");
        startInfo.ArgumentList.Add("info");

        // Unbuffered, so the log file shows how far loading got rather than nothing at all when
        // a model fails to load and the buffer is never flushed.
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new ModelClientException("Windows did not start the Python runtime and gave no reason.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ModelClientException($"Could not start the Python runtime: {ex.Message}", ex);
        }

        children.Track(process, "python-serve");

        var logPath = AppPaths.CreateLogFilePath($"python-{displayName}");
        var instance = new PythonServerInstance(process, modelPath, port, logPath, children);
        instance.BeginCapturingOutput();
        return instance;
    }

    private async Task WaitUntilHealthyAsync(
        PythonServerInstance instance,
        string displayName,
        IProgress<string>? status,
        CancellationToken ct)
    {
        status?.Report($"Loading {displayName} on port {instance.Port}");

        var deadline = DateTime.UtcNow + StartupTimeout;
        var announcedWait = false;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (!instance.IsRunning)
            {
                throw new ModelClientException(
                    $"The Python runtime exited while loading the model. Recent output:{Environment.NewLine}{instance.GetRecentOutput()}");
            }

            if (await IsHealthyAsync(instance, ct).ConfigureAwait(false))
            {
                status?.Report($"Model ready on port {instance.Port}");
                return;
            }

            if (!announcedWait)
            {
                announcedWait = true;
                status?.Report("Waiting for the model to finish loading");
            }

            await Task.Delay(HealthPollInterval, ct).ConfigureAwait(false);
        }

        throw new ModelClientException(
            $"The Python runtime did not become ready within {StartupTimeout.TotalMinutes:0} minutes. See {instance.LogPath}");
    }

    private async Task<bool> IsHealthyAsync(PythonServerInstance instance, CancellationToken ct)
    {
        try
        {
            using var response = await _health.GetAsync(instance.HealthUrl, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            // The socket is not accepting connections yet, which is the normal state while loading.
            return false;
        }
    }

    /// <summary>
    /// Asks the operating system for an unused loopback port, the same way the llama.cpp runtime
    /// does, with the same small race between releasing it and the server binding it.
    /// </summary>
    private static int ReserveFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
