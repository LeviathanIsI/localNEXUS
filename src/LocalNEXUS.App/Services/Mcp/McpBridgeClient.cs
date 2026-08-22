using System.IO;
using System.IO.Pipes;
using System.Text.Json;

namespace LocalNEXUS.App.Services.Mcp;

/// <summary>
/// The stdio host's end of the pipe: one call out, one answer back.
/// </summary>
/// <remarks>
/// Compiled into the host as well as the application, so the two ends cannot disagree about the
/// format, and tested from the application because the interesting case is the one where nobody is
/// listening.
///
/// Nothing here starts the application. A tool call that silently launched a window would be a tool
/// call that opened somebody's project and warmed a model because a language model decided to ask
/// what was running, and starting an application is not a thing to do without being asked.
/// </remarks>
public sealed class McpBridgeClient
{
    private readonly string _pipeName;
    private readonly TimeSpan _connectTimeout;

    public McpBridgeClient(string? pipeName = null, TimeSpan? connectTimeout = null)
    {
        _pipeName = pipeName ?? McpBridge.PipeName;
        _connectTimeout = connectTimeout ?? McpBridge.ConnectTimeout;
    }

    /// <summary>
    /// Sends one tool call, and says plainly when there is nothing to send it to.
    /// </summary>
    /// <remarks>
    /// Never throws for the ordinary failures. Nothing listening, a pipe that closes mid
    /// conversation and an answer that will not parse are all reported as a refusal with a sentence,
    /// because the caller is a language model reading text.
    /// </remarks>
    public async Task<McpBridgeReply> CallAsync(McpBridgeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            using var connecting = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connecting.CancelAfter(_connectTimeout);

            await pipe.ConnectAsync(connecting.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return McpBridgeReply.Refused(McpBridge.NoInstanceMessage);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return McpBridgeReply.Refused(McpBridge.NoInstanceMessage);
        }

        try
        {
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(pipe, leaveOpen: true);

            await writer.WriteLineAsync(JsonSerializer.Serialize(request, McpBridge.Json)).ConfigureAwait(false);

            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(line))
            {
                return McpBridgeReply.Refused(
                    "LocalNEXUS accepted the call and then said nothing. It may have been closed part way through.");
            }

            return JsonSerializer.Deserialize<McpBridgeReply>(line, McpBridge.Json)
                   ?? McpBridgeReply.Refused("LocalNEXUS answered with something that could not be read.");
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or JsonException)
        {
            return McpBridgeReply.Refused($"The call to LocalNEXUS did not complete: {ex.Message}");
        }
    }
}
