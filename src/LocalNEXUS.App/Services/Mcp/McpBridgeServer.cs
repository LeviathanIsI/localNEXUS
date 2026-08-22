using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using LocalNEXUS.App.Infrastructure;

namespace LocalNEXUS.App.Services.Mcp;

/// <summary>
/// Answers tool calls from the stdio host, over a named pipe, while the application is running.
/// </summary>
/// <remarks>
/// The reason there is a pipe at all. MCP is a stdio protocol and this is a window: a second
/// process starting up to answer a tool call would have no project open, no graph, no model server
/// warm and no history. So the stdio end is a small host that owns nothing, and the tool call
/// travels to the process that has the state.
///
/// A named pipe rather than a socket, because it needs no port, no listener registration and no
/// firewall exception, and because Windows scopes it to the machine by construction rather than by
/// binding to loopback and hoping. It is the same mechanism Unity's own relay uses for the same
/// reason.
///
/// One connection per call. A tool call is a request and a reply and nothing else, and a
/// long lived connection would have to be kept alive across a run that takes minutes, which is
/// exactly the thing the run handle exists to avoid.
///
/// Off unless it is switched on. An application that answers to anything able to spawn a process is
/// a different security posture from one that does not, and it is not this code's place to decide
/// that for somebody.
/// </remarks>
public sealed class McpBridgeServer : IDisposable
{
    private readonly McpToolSurface _tools;
    private readonly IActivityFeed _feed;

    private CancellationTokenSource? _stopping;
    private Task? _listening;

    public McpBridgeServer(McpToolSurface tools, IActivityFeed feed)
    {
        _tools = tools;
        _feed = feed;
    }

    /// <summary>True while the pipe is being listened on.</summary>
    public bool IsListening => _listening is { IsCompleted: false };

    /// <summary>Begins listening, or does nothing when it already is.</summary>
    public void Start()
    {
        if (IsListening)
        {
            return;
        }

        _stopping?.Dispose();
        _stopping = new CancellationTokenSource();

        var token = _stopping.Token;
        _listening = Task.Run(() => ListenAsync(token), token);

        _feed.Info(
            "MCP server on",
            $"Answering tool calls on {McpBridge.PipeName}. A caller can open a project, open a graph "
            + "and run it. It cannot write a file except through the graph, and it cannot read a key.");
    }

    /// <summary>Stops listening. Anything mid call finishes first.</summary>
    public void Stop()
    {
        if (_stopping is null)
        {
            return;
        }

        _stopping.Cancel();
        _feed.Info("MCP server off", "Tool calls are no longer answered.");
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Rebuilt per connection rather than kept open. A pipe server instance serves one
                // client and this one serves one call, so the lifetimes are the same thing.
                using var pipe = new NamedPipeServerStream(
                    McpBridge.PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                await ServeAsync(pipe, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or UnauthorizedAccessException)
            {
                // One conversation that went wrong is one conversation. The listener carries on,
                // because the alternative is that a client crashing takes the server with it.
            }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using var reader = new StreamReader(pipe, leaveOpen: true);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        McpBridgeReply reply;

        try
        {
            var request = JsonSerializer.Deserialize<McpBridgeRequest>(line, McpBridge.Json);

            reply = request is null
                ? McpBridgeReply.Refused("The request could not be read.")
                : await _tools.InvokeAsync(request, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            reply = McpBridgeReply.Refused($"The request was not valid JSON: {ex.Message}");
        }

        await writer.WriteLineAsync(JsonSerializer.Serialize(reply, McpBridge.Json)).ConfigureAwait(false);

        // The host reads one line and goes. Waiting for it to drain first is what stops the reply
        // being lost when this end disposes.
        pipe.WaitForPipeDrain();
    }

    public void Dispose()
    {
        Stop();
        _stopping?.Dispose();
        _stopping = null;
    }
}
