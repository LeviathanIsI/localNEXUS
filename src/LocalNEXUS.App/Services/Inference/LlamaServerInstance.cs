using System.Diagnostics;
using System.IO;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// One llama-server child process serving one GGUF file.
/// </summary>
/// <remarks>
/// The process writes nothing to a console. Its standard output and error are pumped into a log
/// file under the user data folder, which is also where the manager looks when a server dies
/// during startup and the failure has to be explained to the user.
/// </remarks>
public sealed class LlamaServerInstance : IDisposable
{
    private const int RetainedLogLines = 40;

    private readonly Queue<string> _recentOutput = new();
    private readonly object _sync = new();

    private StreamWriter? _log;
    private bool _disposed;

    public LlamaServerInstance(
        Process process,
        string ggufPath,
        int port,
        string logPath,
        IReadOnlyList<string> rpcEndpoints)
    {
        Process = process;
        GgufPath = ggufPath;
        Port = port;
        LogPath = logPath;
        RpcEndpoints = rpcEndpoints;
    }

    /// <summary>The rpc workers this server is connected to. Empty for a purely local server.</summary>
    public IReadOnlyList<string> RpcEndpoints { get; }

    /// <summary>The running child process.</summary>
    public Process Process { get; }

    /// <summary>The model this server was started for.</summary>
    public string GgufPath { get; }

    /// <summary>The loopback port the server is listening on.</summary>
    public int Port { get; }

    /// <summary>Where this server's output is being written.</summary>
    public string LogPath { get; }

    /// <summary>Root of this server's OpenAI compatible API.</summary>
    public string BaseUrl => $"http://127.0.0.1:{Port}/v1";

    /// <summary>The health endpoint polled while the model loads.</summary>
    public string HealthUrl => $"http://127.0.0.1:{Port}/health";

    /// <summary>True while the process is still alive.</summary>
    public bool IsRunning
    {
        get
        {
            try
            {
                return !Process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>Starts pumping the child process output into the log file.</summary>
    public void BeginCapturingOutput()
    {
        _log = new StreamWriter(
            new FileStream(LogPath, FileMode.Create, FileAccess.Write, FileShare.Read),
            System.Text.Encoding.UTF8)
        {
            AutoFlush = true
        };

        Process.OutputDataReceived += OnOutputDataReceived;
        Process.ErrorDataReceived += OnOutputDataReceived;
        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
    }

    /// <summary>The last few lines the server produced, used to explain a startup failure.</summary>
    public string GetRecentOutput()
    {
        lock (_sync)
        {
            return string.Join(Environment.NewLine, _recentOutput);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Process.OutputDataReceived -= OnOutputDataReceived;
        Process.ErrorDataReceived -= OnOutputDataReceived;

        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            // The process is already gone, which is the outcome we wanted.
        }

        Process.Dispose();
        _log?.Dispose();
        _log = null;
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return;
        }

        lock (_sync)
        {
            _recentOutput.Enqueue(e.Data);
            while (_recentOutput.Count > RetainedLogLines)
            {
                _recentOutput.Dequeue();
            }

            try
            {
                _log?.WriteLine(e.Data);
            }
            catch (IOException)
            {
                // Losing a log line must never take down a run.
            }
        }
    }
}
