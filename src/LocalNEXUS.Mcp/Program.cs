using System.Text.Json;
using LocalNEXUS.App.Services.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// The MCP server, as a client sees it. Everything it advertises comes from McpToolSurface.Tools,
// which is the same list the application dispatches on, so a tool cannot be offered here that
// nothing answers there.
//
// Nothing is written to standard output except the protocol. A stray line on stdout is a parse
// error at the other end, which is why anything worth saying goes to standard error.

var options = new McpServerOptions
{
    ServerInfo = new Implementation
    {
        Name = "localnexus",
        Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"
    },

    ServerInstructions =
        "Drives a running LocalNEXUS: open a codebase, open a graph of models, run it against a "
        + "request, and read what it did. Start with localnexus_status. A run takes seconds to "
        + "minutes and returns a handle, so poll localnexus_run_result rather than waiting. Files "
        + "are only ever written by the graph's Output node, inside the open project and subject to "
        + "its write rules; there is no tool that writes a file and none that reads a credential."
};

foreach (var tool in McpToolSurface.Tools)
{
    options.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>();
    options.ToolCollection.Add(Relay(tool));
}

await using var transport = new StdioServerTransport(options);
await using var server = McpServer.Create(transport, options, loggerFactory: null, serviceProvider: null);

await server.RunAsync(CancellationToken.None);

return 0;

// One tool, whose entire implementation is to hand the call to the application and hand the answer
// back. The host makes no decisions: it does not validate arguments, because the surface that
// answers them is the one that knows what they mean, and two validators would eventually disagree.
static McpServerTool Relay(McpToolDescription description)
{
    return McpServerTool.Create(
        async (RequestContext<CallToolRequestParams> context, CancellationToken ct) =>
        {
            // The whole arguments object, taken from the request rather than bound parameter by
            // parameter. The surface that answers is the one that knows what each tool's arguments
            // mean, and binding them here would be a second opinion that could disagree with it.
            var arguments = JsonSerializer.SerializeToElement(
                context.Params?.Arguments ?? new Dictionary<string, JsonElement>(),
                McpBridge.Json);

            var bridge = new McpBridgeClient();

            var reply = await bridge
                .CallAsync(new McpBridgeRequest(description.Name, arguments), ct)
                .ConfigureAwait(false);

            // A refusal comes back as an error result rather than as prose that reads like success,
            // so a client can tell the two apart without parsing English.
            return new CallToolResult
            {
                IsError = !reply.Ok,
                Content = [new TextContentBlock { Text = reply.Text }]
            };
        },
        new McpServerToolCreateOptions
        {
            Name = description.Name,
            Description = description.Description,
            Title = description.Name
        });
}
