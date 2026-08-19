using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Owns the llama-server child processes that serve local GGUF models.
/// </summary>
/// <remarks>
/// One server is started per model file and then reused for every later request, because
/// loading a large model onto the GPU is by far the most expensive part of a local run. Servers
/// are started silently with no console window and are killed when the application exits.
/// </remarks>
public sealed class LlamaServerManager : IDisposable
{
    /// <summary>Context window passed to every server.</summary>
    private const int ContextSize = 8192;

    /// <summary>
    /// Layers to offload to the GPU. The value is deliberately larger than any real model's
    /// layer count, which llama.cpp treats as "offload everything".
    /// </summary>
    private const int GpuLayers = 999;

    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, LlamaServerInstance> _servers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly HttpClient _health = new() { Timeout = TimeSpan.FromSeconds(5) };

    private bool _disposed;

    /// <summary>
    /// Returns the base URL of a running server for the given model, starting one if needed.
    /// </summary>
    /// <param name="ggufPath">Absolute path of the GGUF file to serve.</param>
    /// <param name="status">Receives progress messages while the model loads.</param>
    /// <param name="ct">Cancels the wait. A server that is already loading keeps loading.</param>
    /// <exception cref="ModelClientException">The executable or model is missing, or the server failed to become healthy.</exception>
    public async Task<string> EnsureServerAsync(string ggufPath, IProgress<string>? status, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(ggufPath))
        {
            throw new ModelClientException("No local model is selected for this node.");
        }

        if (!File.Exists(ggufPath))
        {
            throw new ModelClientException($"The model file no longer exists: {ggufPath}");
        }

        var key = Path.GetFullPath(ggufPath);

        // Serialised so that two nodes asking for the same model at once start one server, not two.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_servers.TryGetValue(key, out var existing))
            {
                if (existing.IsRunning)
                {
                    status?.Report($"Reusing llama-server on port {existing.Port}");
                    return existing.BaseUrl;
                }

                existing.Dispose();
                _servers.Remove(key);
            }

            var instance = StartServer(key);
            _servers[key] = instance;

            try
            {
                await WaitUntilHealthyAsync(instance, status, ct).ConfigureAwait(false);
            }
            catch
            {
                instance.Dispose();
                _servers.Remove(key);
                throw;
            }

            return instance.BaseUrl;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Stops every running server. Called when the application exits.</summary>
    public void ShutdownAll()
    {
        _gate.Wait();
        try
        {
            foreach (var server in _servers.Values)
            {
                server.Dispose();
            }

            _servers.Clear();
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
        ShutdownAll();
        _gate.Dispose();
        _health.Dispose();
    }

    private static LlamaServerInstance StartServer(string ggufPath)
    {
        var executable = AppPaths.FindLlamaServerExecutable()
            ?? throw new ModelClientException(BuildMissingExecutableMessage());

        var port = ReserveFreePort();
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add(ggufPath);
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString());
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(ContextSize.ToString());
        startInfo.ArgumentList.Add("-ngl");
        startInfo.ArgumentList.Add(GpuLayers.ToString());

        Process process;
        try
        {
            process = Process.Start(startInfo)
                ?? throw new ModelClientException("Windows did not start llama-server and gave no reason.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new ModelClientException($"Could not start llama-server: {ex.Message}", ex);
        }

        var logPath = AppPaths.CreateLogFilePath($"llama-{Path.GetFileNameWithoutExtension(ggufPath)}");
        var instance = new LlamaServerInstance(process, ggufPath, port, logPath);
        instance.BeginCapturingOutput();
        return instance;
    }

    private async Task WaitUntilHealthyAsync(
        LlamaServerInstance instance,
        IProgress<string>? status,
        CancellationToken ct)
    {
        status?.Report($"Loading {Path.GetFileNameWithoutExtension(instance.GgufPath)} on port {instance.Port}");

        var deadline = DateTime.UtcNow + StartupTimeout;
        var announcedWait = false;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            if (!instance.IsRunning)
            {
                throw new ModelClientException(
                    $"llama-server exited while loading the model. Recent output:{Environment.NewLine}{instance.GetRecentOutput()}");
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
            $"llama-server did not become ready within {StartupTimeout.TotalMinutes:0} minutes. See {instance.LogPath}");
    }

    private async Task<bool> IsHealthyAsync(LlamaServerInstance instance, CancellationToken ct)
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
    /// Asks the operating system for an unused loopback port. There is a small race between
    /// releasing the port and llama-server binding it, which is acceptable here because the
    /// alternative is a fixed port that collides with a second instance of the app.
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

    private static string BuildMissingExecutableMessage()
    {
        var searched = string.Join(
            Environment.NewLine,
            AppPaths.EnumerateLlamaSearchDirectories().Distinct(StringComparer.OrdinalIgnoreCase).Take(4));

        return $"{AppPaths.LlamaServerExecutableName} was not found. Place a llama.cpp build in vendor\\llama. Searched:{Environment.NewLine}{searched}";
    }
}
