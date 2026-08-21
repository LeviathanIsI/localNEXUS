using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// Newline delimited JSON-RPC 2.0 over a child process's standard input and output.
/// </summary>
/// <remarks>
/// The same framing MCP uses, which is the whole reason it was chosen for the node contract as
/// well: one transport to implement, one to debug, and an author who has written an MCP server
/// already knows the shape.
/// <para>
/// The rule that keeps this working is that stdout carries protocol and nothing else. A single
/// stray line of logging on stdout desynchronises the stream and produces a parse error that
/// looks like the extension is broken in some deep way, when it printed "starting up". So stderr
/// is drained separately to a log file and never parsed, and that log is what the panel links to.
/// A line on stdout that is not JSON is reported as exactly that rather than being swallowed.
/// </para>
/// <para>
/// Reading runs on its own pump rather than per request, because responses may arrive out of
/// order and notifications arrive unasked. Each request parks a completion source under its id
/// and the pump completes it.
/// </para>
/// </remarks>
public sealed class JsonRpcConnection : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _writer;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    private long _nextId;
    private bool _disposed;

    public JsonRpcConnection(Process process)
    {
        _process = process;
        _writer = process.StandardInput;

        PumpTask = Task.Run(() => PumpAsync(_shutdown.Token));
    }

    /// <summary>Raised for a notification the other end sent unasked, such as progress.</summary>
    public event Action<string, JsonNode?>? NotificationReceived;

    /// <summary>Raised when a line arrives on stdout that is not JSON, which is a protocol violation.</summary>
    public event Action<string>? ProtocolViolation;

    /// <summary>The read pump, so a caller can observe it ending.</summary>
    public Task PumpTask { get; }

    /// <summary>True while the process is alive and the pump is running.</summary>
    public bool IsAlive => !_disposed && !_process.HasExited;

    /// <summary>
    /// Sends a request and waits for its response.
    /// </summary>
    /// <exception cref="ExtensionException">
    /// The process is gone, the call timed out, or the other end returned an error.
    /// </exception>
    public async Task<JsonNode?> InvokeAsync(
        string method,
        JsonObject? parameters,
        TimeSpan timeout,
        CancellationToken ct)
    {
        if (!IsAlive)
        {
            throw new ExtensionException(
                $"The extension is not running, so '{method}' could not be sent. Check its log for why it stopped.");
        }

        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;

        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method
        };

        if (parameters is not null)
        {
            envelope["params"] = parameters;
        }

        try
        {
            await SendAsync(envelope, ct).ConfigureAwait(false);

            using var timer = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, ct);
            await using var registration = linked.Token.Register(() =>
            {
                // A timeout and a cancellation are different failures and must not read the same.
                if (timer.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    completion.TrySetException(new ExtensionException(
                        $"The extension did not answer '{method}' within {timeout.TotalSeconds:0} seconds."));
                }
                else
                {
                    completion.TrySetCanceled(ct);
                }
            }).ConfigureAwait(false);

            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>Sends a notification, which by definition has no reply to wait for.</summary>
    public async Task NotifyAsync(string method, JsonObject? parameters, CancellationToken ct)
    {
        if (!IsAlive)
        {
            return;
        }

        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };

        if (parameters is not null)
        {
            envelope["params"] = parameters;
        }

        try
        {
            await SendAsync(envelope, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // A notification nobody is waiting on is not worth faulting a run over.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();

        foreach (var pending in _pending.Values)
        {
            pending.TrySetException(new ExtensionException("The extension was shut down while a call was in flight."));
        }

        _pending.Clear();
        _shutdown.Dispose();
        _writeGate.Dispose();
    }

    private async Task SendAsync(JsonObject envelope, CancellationToken ct)
    {
        var line = envelope.ToJsonString();

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
            await _writer.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            throw new ExtensionException(
                "The extension closed its input before the request could be sent. It has probably exited.", ex);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        var reader = _process.StandardOutput;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Dispatch(line);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The process went away. Everything waiting is failed below.
        }
        finally
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(new ExtensionException(
                    "The extension stopped responding and its output stream closed. Check its log."));
            }

            _pending.Clear();
        }
    }

    private void Dispatch(string line)
    {
        JsonNode? message;

        try
        {
            message = JsonNode.Parse(line);
        }
        catch (JsonException)
        {
            // Almost always an extension logging to stdout. Say so, because the symptom otherwise
            // looks like a broken protocol rather than a misdirected print statement.
            ProtocolViolation?.Invoke(line);
            return;
        }

        if (message is not JsonObject envelope)
        {
            ProtocolViolation?.Invoke(line);
            return;
        }

        if (envelope["id"] is { } idNode && idNode.GetValueKind() is JsonValueKind.Number)
        {
            var id = idNode.GetValue<long>();

            if (!_pending.TryGetValue(id, out var completion))
            {
                return;
            }

            if (envelope["error"] is JsonObject error)
            {
                var code = error["code"]?.GetValue<int>();
                var text = error["message"]?.GetValue<string>() ?? "no message";
                completion.TrySetException(new ExtensionException(
                    code is null ? text : $"{text} (error {code})"));
                return;
            }

            completion.TrySetResult(envelope["result"]);
            return;
        }

        if (envelope["method"]?.GetValue<string>() is { } method)
        {
            NotificationReceived?.Invoke(method, envelope["params"]);
        }
    }
}
