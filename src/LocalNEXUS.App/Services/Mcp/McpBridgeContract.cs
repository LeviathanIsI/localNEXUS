using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalNEXUS.App.Services.Mcp;

/// <summary>
/// The one thing the stdio host and the running application both have to agree about.
/// </summary>
/// <remarks>
/// This file is compiled into both. The host is a plain console executable and the application is
/// WPF, so they cannot share an assembly without dragging WPF into a process whose whole job is to
/// read stdin, and a copy in each would be two definitions of a wire format that has to match
/// exactly. Linking one source file into two projects is the version of sharing that has neither
/// problem.
///
/// A request is a tool name and its arguments; a reply is whether it worked and what to say. It is
/// deliberately not JSON-RPC and deliberately not MCP: the MCP conversation happens on the host's
/// stdin, and what crosses the pipe is only what is left after the protocol has been dealt with.
/// One request, one reply, one connection.
/// </remarks>
public static class McpBridge
{
    /// <summary>
    /// The pipe the running application listens on.
    /// </summary>
    /// <remarks>
    /// Per user rather than machine wide. A named pipe with a fixed name is reachable by every
    /// session on the machine, and this one can run a graph, so it is scoped to the account that
    /// owns the application and the projects it writes into.
    /// </remarks>
    public static string PipeName => $"LocalNEXUS.mcp.{Environment.UserName}";

    /// <summary>How long the host waits for the application to accept a connection.</summary>
    public static TimeSpan ConnectTimeout { get; } = TimeSpan.FromSeconds(3);

    /// <summary>What both ends serialise with.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>What the host says when nothing is listening.</summary>
    /// <remarks>
    /// Written down here so the host and the tests say the same thing. A caller that gets this has
    /// not hit a bug: the application is simply not running, or is running with the server switched
    /// off, and both are ordinary.
    /// </remarks>
    public const string NoInstanceMessage =
        "LocalNEXUS is not running, or its MCP server is switched off. Start LocalNEXUS and turn on "
        + "\"Answer MCP tool calls\" in Settings. Nothing was started on your behalf, because a graph "
        + "run writes into a project and that is not something to begin without being asked.";
}

/// <summary>One tool call, on its way to the application.</summary>
/// <param name="Tool">Which tool, by its MCP name.</param>
/// <param name="Arguments">Its arguments, exactly as the MCP client sent them.</param>
public sealed record McpBridgeRequest(string Tool, JsonElement? Arguments);

/// <summary>What the application answered.</summary>
/// <param name="Ok">False when the tool refused or failed, which the host reports as an error.</param>
/// <param name="Text">What to show the caller, whether it worked or not.</param>
public sealed record McpBridgeReply(bool Ok, string Text)
{
    /// <summary>A refusal, worded by whatever refused.</summary>
    public static McpBridgeReply Refused(string why) => new(false, why);

    /// <summary>An answer.</summary>
    public static McpBridgeReply Answer(string text) => new(true, text);
}
