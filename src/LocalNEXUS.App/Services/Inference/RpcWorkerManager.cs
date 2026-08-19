using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Runs the bundled rpc-server so this install can serve model sections to another
/// orchestrator. The counterpart of <see cref="LlamaServerManager"/>: any install can
/// orchestrate or contribute, there is no dedicated worker machine.
/// </summary>
/// <remarks>
/// The process is started silently with its output captured to the logs folder, and it binds
/// all interfaces because being reachable from the network is the point of contributing.
/// Stopping is graceful where the protocol allows: rpc-server has no drain command, so a stop
/// waits for the active connections it reports on its output to close before the process is
/// killed, except on application exit where it is killed outright.
///
/// The memory this machine offers is declared on its source entry and honoured by
/// orchestrators through their split proportions. The bundled rpc-server build exposes no
/// flag that would enforce a cap on the worker side.
/// </remarks>
public sealed partial class RpcWorkerManager : ObservableObject, IDisposable
{
    private const int RetainedLogLines = 40;

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ReadyPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan GracefulStopTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GracefulStopPollInterval = TimeSpan.FromMilliseconds(500);

    private readonly AppConfig _config;
    private readonly IActivityFeed _feed;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private readonly Queue<string> _recentOutput = new();

    private Process? _process;
    private StreamWriter? _log;
    private int _activeConnections;
    private bool _stopRequested;
    private bool _disposed;

    /// <summary>True while the rpc-server child process is serving.</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>One line for the panel: what the worker is doing right now.</summary>
    [ObservableProperty]
    private string _statusText = "Not contributing";

    /// <summary>The port the rpc-server listens on. Persisted.</summary>
    [ObservableProperty]
    private int _port;

    public RpcWorkerManager(AppConfig config, IActivityFeed feed)
    {
        _config = config;
        _feed = feed;
        _port = config.WorkerPort is >= 1 and <= 65535 ? config.WorkerPort : 50052;
    }

    /// <summary>
    /// Restores the persisted contribution state at startup, reporting a failure to the feed
    /// rather than letting it interrupt composition.
    /// </summary>
    public async Task RestoreAsync()
    {
        if (!_config.ContributeEnabled)
        {
            return;
        }

        try
        {
            await StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (ModelClientException ex)
        {
            _feed.Error("Contribution not restored", ex.Message);
        }
    }

    /// <summary>Starts the rpc-server and waits until its socket accepts connections.</summary>
    /// <exception cref="ModelClientException">The executable is missing or the worker did not become ready.</exception>
    public async Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_process is { HasExited: false })
            {
                return;
            }

            var executable = AppPaths.FindRpcServerExecutable()
                ?? throw new ModelClientException(
                    $"{AppPaths.RpcServerExecutableName} was not found. Place a llama.cpp build in vendor\\llama to contribute this machine.");

            StatusText = $"Starting rpc-server on port {Port}";

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            // Bound to all interfaces deliberately: being reachable from the network is the
            // point. The health of the socket is what other orchestrators probe.
            startInfo.ArgumentList.Add("-H");
            startInfo.ArgumentList.Add("0.0.0.0");
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(Port.ToString());

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new ModelClientException("Windows did not start rpc-server and gave no reason.");
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                throw new ModelClientException($"Could not start rpc-server: {ex.Message}", ex);
            }

            lock (_sync)
            {
                _recentOutput.Clear();
                _activeConnections = 0;
            }

            _stopRequested = false;
            _process = process;
            _log = new StreamWriter(
                new FileStream(AppPaths.CreateLogFilePath("rpc-worker"), FileMode.Create, FileAccess.Write, FileShare.Read),
                System.Text.Encoding.UTF8)
            {
                AutoFlush = true
            };

            process.OutputDataReceived += OnOutputDataReceived;
            process.ErrorDataReceived += OnOutputDataReceived;
            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await WaitUntilAcceptingAsync(process, ct).ConfigureAwait(false);
            }
            catch
            {
                TearDownProcess();
                StatusText = "Not contributing";
                throw;
            }

            IsRunning = true;
            StatusText = $"Serving on port {Port}";

            _config.ContributeEnabled = true;
            _config.WorkerPort = Port;
            _config.Save();

            _feed.Info("Contribution started", $"rpc-server is listening on port {Port}.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Stops contributing. Waits for active connections to close before killing the process,
    /// up to a bounded timeout, so a request routing through this machine is not cut mid
    /// stream by a toggle.
    /// </summary>
    public async Task StopAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_process is null)
            {
                return;
            }

            _stopRequested = true;

            var deadline = DateTime.UtcNow + GracefulStopTimeout;
            while (DateTime.UtcNow < deadline && _process is { HasExited: false })
            {
                int active;
                lock (_sync)
                {
                    active = _activeConnections;
                }

                if (active == 0)
                {
                    break;
                }

                StatusText = $"Stopping after {active} active connection(s) close";
                await Task.Delay(GracefulStopPollInterval).ConfigureAwait(false);
            }

            TearDownProcess();
            IsRunning = false;
            StatusText = "Not contributing";

            _config.ContributeEnabled = false;
            _config.Save();

            _feed.Info("Contribution stopped", "rpc-server has been shut down.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopRequested = true;

        // Application exit: no drain, nothing may outlive the window.
        TearDownProcess();
        _gate.Dispose();
    }

    private async Task WaitUntilAcceptingAsync(Process process, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                throw new ModelClientException(
                    $"rpc-server exited during startup. Recent output:{Environment.NewLine}{GetRecentOutput()}");
            }

            try
            {
                using var probe = new TcpClient();
                using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
                window.CancelAfter(ReadyPollInterval);
                await probe.ConnectAsync("127.0.0.1", Port, window.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                // Not accepting yet.
            }

            await Task.Delay(ReadyPollInterval, ct).ConfigureAwait(false);
        }

        throw new ModelClientException(
            $"rpc-server did not start accepting connections on port {Port} within {ReadyTimeout.TotalSeconds:0} seconds.");
    }

    private void TearDownProcess()
    {
        var process = _process;
        _process = null;

        if (process is not null)
        {
            process.OutputDataReceived -= OnOutputDataReceived;
            process.ErrorDataReceived -= OnOutputDataReceived;
            process.Exited -= OnProcessExited;

            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
            {
                // Already gone, which is the outcome we wanted.
            }

            process.Dispose();
        }

        _log?.Dispose();
        _log = null;
    }

    private string GetRecentOutput()
    {
        lock (_sync)
        {
            return string.Join(Environment.NewLine, _recentOutput);
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_stopRequested)
        {
            return;
        }

        IsRunning = false;
        StatusText = "rpc-server exited unexpectedly";
        _feed.Error(
            "Contribution interrupted",
            $"rpc-server exited on its own. Recent output:{Environment.NewLine}{GetRecentOutput()}");
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return;
        }

        int? active = null;
        lock (_sync)
        {
            _recentOutput.Enqueue(e.Data);
            while (_recentOutput.Count > RetainedLogLines)
            {
                _recentOutput.Dequeue();
            }

            // The only visibility rpc-server gives into its work is these two lines, and they
            // are what makes a graceful stop possible at all.
            if (e.Data.Contains("Accepted client connection", StringComparison.OrdinalIgnoreCase))
            {
                _activeConnections++;
                active = _activeConnections;
            }
            else if (e.Data.Contains("Client connection closed", StringComparison.OrdinalIgnoreCase))
            {
                _activeConnections = Math.Max(0, _activeConnections - 1);
                active = _activeConnections;
            }

            try
            {
                _log?.WriteLine(e.Data);
            }
            catch (IOException)
            {
                // Losing a log line must never take down the worker.
            }
        }

        if (active is { } count && IsRunning)
        {
            StatusText = count > 0
                ? $"Serving on port {Port}, {count} active connection(s)"
                : $"Serving on port {Port}";
        }
    }

    partial void OnPortChanged(int value)
    {
        if (value is >= 1 and <= 65535)
        {
            _config.WorkerPort = value;
            _config.Save();
        }
    }
}
