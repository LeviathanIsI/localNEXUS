using System.Diagnostics;
using System.IO;
using LocalNEXUS.App.Services.Processes;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// One Python child process serving one safetensors model.
/// </summary>
/// <remarks>
/// The same shape as a llama-server instance, for the same reasons: no console window, output
/// pumped into a log file that explains a startup failure, and stopping delegated to the child
/// process group rather than done here. The one difference that matters to callers is the model
/// id, which this server refuses to accept in any form but the exact path it was pinned to.
/// </remarks>
public sealed class PythonServerInstance : IDisposable
{
    private const int RetainedLogLines = 40;

    private readonly Queue<string> _recentOutput = new();
    private readonly object _sync = new();

    private readonly ChildProcessGroup _children;

    private StreamWriter? _log;
    private bool _disposed;

    public PythonServerInstance(Process process, string modelPath, int port, string logPath, ChildProcessGroup children)
    {
        Process = process;
        ModelPath = modelPath;
        Port = port;
        LogPath = logPath;
        _children = children;
    }

    /// <summary>The running child process.</summary>
    public Process Process { get; }

    /// <summary>The model folder this server was pinned to.</summary>
    public string ModelPath { get; }

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

        _children.Terminate(Process);

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
