using System.Diagnostics;
using LocalNEXUS.App.Models.Extensions;
using ModelContextProtocol.Client;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// One running extension process, together with whichever protocol is being spoken over it.
/// </summary>
/// <remarks>
/// A session belongs to one contract rather than to one extension, because a process has exactly
/// one pair of standard streams and a protocol client owns them completely. The MCP SDK's
/// transport reads stdout itself, and the node contract's connection reads stdout itself, and
/// there is no way for both to do that to the same process without one of them losing messages.
/// <para>
/// So an extension declaring both contracts gets two processes. That is the honest arrangement:
/// it costs one extra process in a case that is currently hypothetical, and the alternative is
/// multiplexing two protocols down one pipe, which would be a bespoke framing that no extension
/// author has ever seen and that neither the MCP SDK nor anything else could speak.
/// </para>
/// </remarks>
public sealed class ExtensionSession : IDisposable
{
    private bool _disposed;

    public ExtensionSession(
        string extensionId,
        ExtensionContract contract,
        Process process,
        string logPath,
        JsonRpcConnection? rpc,
        McpClient? mcp)
    {
        ExtensionId = extensionId;
        Contract = contract;
        Process = process;
        LogPath = logPath;
        Rpc = rpc;
        Mcp = mcp;
    }

    /// <summary>Which extension this is.</summary>
    public string ExtensionId { get; }

    /// <summary>Which contract is being spoken over this process.</summary>
    public ExtensionContract Contract { get; }

    /// <summary>The worker process itself.</summary>
    public Process Process { get; }

    /// <summary>Where this session's stderr is being written.</summary>
    public string LogPath { get; }

    /// <summary>The node contract connection, when this is a node session.</summary>
    public JsonRpcConnection? Rpc { get; }

    /// <summary>The MCP client, when this is an MCP session.</summary>
    public McpClient? Mcp { get; }

    /// <summary>True while the process is up.</summary>
    public bool IsAlive
    {
        get
        {
            try
            {
                if (_disposed || Process.HasExited)
                {
                    return false;
                }

                return Rpc is null || Rpc.IsAlive;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Rpc?.Dispose();

        if (Mcp is not null)
        {
            // Disposal talks to the server, which is pointless once it is gone and can hang.
            try
            {
                Mcp.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
            }
            catch (Exception ex) when (ex is AggregateException or ObjectDisposedException or InvalidOperationException)
            {
                // The process is about to be terminated regardless.
            }
        }
    }
}
