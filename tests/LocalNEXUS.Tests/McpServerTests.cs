using System.Text.Json;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Services.Mcp;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The tool surface a caller reaches over MCP.
/// </summary>
/// <remarks>
/// Tested against a stand in for the application rather than against a window, because what is
/// worth pinning is the decision making: which tools exist, what each does with the arguments it is
/// handed, and what it says when the arguments are wrong. The pipe and the stdio host are exercised
/// separately, and the case that matters most there is nobody listening.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class McpServerTests
{
    /// <summary>An application that records what it was asked and answers plausibly.</summary>
    private sealed class FakeApp : IMcpAppSurface
    {
        public List<string> Calls { get; } = new();

        public string? ProjectOpened { get; private set; }

        public string? GraphOpened { get; private set; }

        public string? RequestRun { get; private set; }

        public int RunLimit { get; private set; }

        public bool RunFinished { get; set; } = true;

        public Exception? Throws { get; set; }

        public Task<string> OpenProjectAsync(string path, CancellationToken ct)
        {
            Calls.Add(nameof(OpenProjectAsync));
            Throw();
            ProjectOpened = path;
            return Task.FromResult($"Opened {path}. Detected as a C# project. 6 source file(s) indexed.");
        }

        public Task<string> DescribeStateAsync(CancellationToken ct)
        {
            Calls.Add(nameof(DescribeStateAsync));
            Throw();
            return Task.FromResult("Project: none open.");
        }

        public Task<IReadOnlyList<string>> ListGraphsAsync(CancellationToken ct)
        {
            Calls.Add(nameof(ListGraphsAsync));
            Throw();
            return Task.FromResult<IReadOnlyList<string>>(new[] { "One model, one file (template)" });
        }

        public Task<string> OpenGraphAsync(string name, CancellationToken ct)
        {
            Calls.Add(nameof(OpenGraphAsync));
            Throw();
            GraphOpened = name;
            return Task.FromResult($"Opened {name}: 3 node(s).");
        }

        public Task<McpRunHandle> StartRunAsync(string request, CancellationToken ct)
        {
            Calls.Add(nameof(StartRunAsync));
            Throw();
            RequestRun = request;
            return Task.FromResult(new McpRunHandle("run-1", "Running", false));
        }

        public Task<McpRunHandle> RunStateAsync(CancellationToken ct)
        {
            Calls.Add(nameof(RunStateAsync));
            Throw();
            return Task.FromResult(new McpRunHandle("run-1", RunFinished ? "Completed" : "Running", RunFinished));
        }

        public Task<string> DescribeRunAsync(string? runId, CancellationToken ct)
        {
            Calls.Add(nameof(DescribeRunAsync));
            Throw();
            return Task.FromResult($"Run {runId ?? "run-1"}, Completed. 1 file(s) written.");
        }

        public Task<string> ListRunsAsync(int limit, CancellationToken ct)
        {
            Calls.Add(nameof(ListRunsAsync));
            Throw();
            RunLimit = limit;
            return Task.FromResult("- run-1  Completed");
        }

        private void Throw()
        {
            if (Throws is { } ex)
            {
                throw ex;
            }
        }
    }

    private static McpBridgeRequest Call(string tool, string? argumentsJson = null)
        => new(tool, argumentsJson is null ? null : JsonDocument.Parse(argumentsJson).RootElement.Clone());

    private static async Task<McpBridgeReply> Invoke(FakeApp app, string tool, string? argumentsJson = null)
        => await new McpToolSurface(app).InvokeAsync(Call(tool, argumentsJson), CancellationToken.None);

    /// <summary>Every advertised tool is one the surface actually answers.</summary>
    /// <remarks>
    /// The stdio host builds its tool list from this same collection, so a name here that nothing
    /// dispatches on would be a tool a client could see and never successfully call.
    /// </remarks>
    [Fact]
    public async Task EveryAdvertisedToolIsAnswered()
    {
        Assert.NotEmpty(McpToolSurface.Tools);

        foreach (var tool in McpToolSurface.Tools)
        {
            var reply = await Invoke(new FakeApp(), tool.Name);

            Assert.DoesNotContain("There is no tool called", reply.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>Each tool describes itself and its arguments.</summary>
    [Fact]
    public void EveryToolHasADescriptionAndASchema()
    {
        foreach (var tool in McpToolSurface.Tools)
        {
            Assert.StartsWith("localnexus_", tool.Name, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(tool.Description));

            using var schema = JsonDocument.Parse(tool.Schema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
        }
    }

    /// <summary>
    /// Nothing writes a file and nothing reads a credential.
    /// </summary>
    /// <remarks>
    /// The surface is deliberately a fixed list, so this is a test that the list has not grown a
    /// tool of a kind it was decided not to have. The Output node writes, inside the project and
    /// through the guardrails, and that stays the only path.
    /// </remarks>
    [Fact]
    public void NoToolWritesAFileOrReadsAKey()
    {
        foreach (var tool in McpToolSurface.Tools)
        {
            Assert.DoesNotContain("write", tool.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("delete", tool.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("key", tool.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("credential", tool.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret", tool.Name, StringComparison.OrdinalIgnoreCase);
        }

        // And the interface the tools reach the application through offers nothing of the kind.
        var members = typeof(IMcpAppSurface).GetMethods().Select(m => m.Name).ToList();

        Assert.Equal(8, members.Count);
        Assert.DoesNotContain(members, m => m.Contains("Write", StringComparison.Ordinal));
        Assert.DoesNotContain(members, m => m.Contains("Credential", StringComparison.Ordinal));
    }

    /// <summary>A tool nobody has is refused by name, with the list.</summary>
    [Fact]
    public async Task AnUnknownToolIsRefused()
    {
        var reply = await Invoke(new FakeApp(), "localnexus_delete_everything");

        Assert.False(reply.Ok);
        Assert.Contains("no tool called", reply.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("localnexus_status", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusAsksTheApplication()
    {
        var app = new FakeApp();
        var reply = await Invoke(app, "localnexus_status");

        Assert.True(reply.Ok);
        Assert.Contains(nameof(IMcpAppSurface.DescribeStateAsync), app.Calls);
    }

    [Fact]
    public async Task OpenProjectPassesThePath()
    {
        var app = new FakeApp();
        var reply = await Invoke(app, "localnexus_open_project", """{"path":"C:\\code\\shop"}""");

        Assert.True(reply.Ok);
        Assert.Equal("C:\\code\\shop", app.ProjectOpened);
    }

    [Fact]
    public async Task OpenProjectWithoutAPathIsRefused()
    {
        var app = new FakeApp();

        foreach (var arguments in new[] { null, "{}", """{"path":""}""", """{"path":42}""" })
        {
            var reply = await Invoke(app, "localnexus_open_project", arguments);

            Assert.False(reply.Ok);
            Assert.Contains("needs a 'path'", reply.Text, StringComparison.Ordinal);
        }

        Assert.Null(app.ProjectOpened);
    }

    /// <summary>A folder that is not there comes back as a sentence, not an exception.</summary>
    [Fact]
    public async Task AFolderThatIsNotThereIsReportedNotThrown()
    {
        var app = new FakeApp { Throws = new System.IO.DirectoryNotFoundException("There is no folder at X.") };

        var reply = await Invoke(app, "localnexus_open_project", """{"path":"X"}""");

        Assert.False(reply.Ok);
        Assert.Contains("There is no folder at X.", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListGraphsReportsWhatThereIs()
    {
        var reply = await Invoke(new FakeApp(), "localnexus_list_graphs");

        Assert.True(reply.Ok);
        Assert.Contains("One model, one file", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenGraphPassesTheName()
    {
        var app = new FakeApp();
        var reply = await Invoke(app, "localnexus_open_graph", """{"name":"Check it compiles"}""");

        Assert.True(reply.Ok);
        Assert.Equal("Check it compiles", app.GraphOpened);
    }

    [Fact]
    public async Task OpenGraphWithoutANameIsRefused()
    {
        var app = new FakeApp();
        var reply = await Invoke(app, "localnexus_open_graph", "{}");

        Assert.False(reply.Ok);
        Assert.Contains("needs a 'name'", reply.Text, StringComparison.Ordinal);
        Assert.Null(app.GraphOpened);
    }

    /// <summary>Run starts and hands back a handle rather than waiting.</summary>
    /// <remarks>
    /// The decision this surface is built around. A graph against a local model takes seconds to
    /// minutes, and a call that blocked that long would be abandoned by many clients, leaving the
    /// run going with nobody able to read what it did.
    /// </remarks>
    [Fact]
    public async Task RunStartsAndReturnsAHandle()
    {
        var app = new FakeApp();
        var reply = await Invoke(app, "localnexus_run", """{"request":"Add a Slug helper."}""");

        Assert.True(reply.Ok);
        Assert.Equal("Add a Slug helper.", app.RequestRun);
        Assert.Contains("run-1", reply.Text, StringComparison.Ordinal);
        Assert.Contains("Poll localnexus_run_result", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunWithoutARequestIsRefused()
    {
        var app = new FakeApp();
        var reply = await Invoke(app, "localnexus_run", """{"request":"   "}""");

        Assert.False(reply.Ok);
        Assert.Contains("needs a 'request'", reply.Text, StringComparison.Ordinal);
        Assert.Null(app.RequestRun);
    }

    /// <summary>A run already going is refused with the reason rather than queued.</summary>
    [Fact]
    public async Task ASecondRunIsRefusedWhileOneIsGoing()
    {
        var app = new FakeApp { Throws = new InvalidOperationException("a run already in progress has to finish first") };

        var reply = await Invoke(app, "localnexus_run", """{"request":"again"}""");

        Assert.False(reply.Ok);
        Assert.Contains("already in progress", reply.Text, StringComparison.Ordinal);
    }

    /// <summary>Asking for the result while it is still going says so and does not block.</summary>
    [Fact]
    public async Task ResultWhileRunningSaysStillRunning()
    {
        var app = new FakeApp { RunFinished = false };
        var reply = await Invoke(app, "localnexus_run_result");

        Assert.True(reply.Ok);
        Assert.Contains("Still running", reply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IMcpAppSurface.DescribeRunAsync), app.Calls);
    }

    [Fact]
    public async Task ResultOnceFinishedDescribesTheRun()
    {
        var app = new FakeApp { RunFinished = true };
        var reply = await Invoke(app, "localnexus_run_result");

        Assert.True(reply.Ok);
        Assert.Contains("Completed", reply.Text, StringComparison.Ordinal);
        Assert.Contains(nameof(IMcpAppSurface.DescribeRunAsync), app.Calls);
    }

    /// <summary>A named run is read from the history without consulting the live state.</summary>
    [Fact]
    public async Task AskingForANamedRunSkipsTheLiveState()
    {
        var app = new FakeApp { RunFinished = false };
        var reply = await Invoke(app, "localnexus_run_result", """{"run_id":"run-7"}""");

        Assert.True(reply.Ok);
        Assert.Contains("run-7", reply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IMcpAppSurface.RunStateAsync), app.Calls);
    }

    [Fact]
    public async Task ListRunsDefaultsAndClamps()
    {
        var app = new FakeApp();

        await Invoke(app, "localnexus_list_runs");
        Assert.Equal(20, app.RunLimit);

        await Invoke(app, "localnexus_list_runs", """{"limit":5}""");
        Assert.Equal(5, app.RunLimit);

        await Invoke(app, "localnexus_list_runs", """{"limit":100000}""");
        Assert.Equal(McpToolSurface.MaximumRunLimit, app.RunLimit);
    }

    [Fact]
    public async Task ListRunsRefusesNonsense()
    {
        var app = new FakeApp();

        foreach (var arguments in new[] { """{"limit":"lots"}""", """{"limit":0}""", """{"limit":-3}""" })
        {
            var reply = await Invoke(app, "localnexus_list_runs", arguments);
            Assert.False(reply.Ok);
        }
    }

    /// <summary>
    /// Nothing running is said plainly, and nothing is started.
    /// </summary>
    /// <remarks>
    /// The case the whole two process arrangement turns on. A tool call that silently launched the
    /// application would be a tool call that opened somebody's project and warmed a model because a
    /// language model wondered what was running.
    /// </remarks>
    [Fact]
    public async Task NoInstanceRunningIsSaidPlainly()
    {
        var client = new McpBridgeClient(
            pipeName: "LocalNEXUS.tests.nobody." + Guid.NewGuid().ToString("N"),
            connectTimeout: TimeSpan.FromMilliseconds(250));

        var reply = await client.CallAsync(
            new McpBridgeRequest("localnexus_status", null),
            CancellationToken.None);

        Assert.False(reply.Ok);
        Assert.Equal(McpBridge.NoInstanceMessage, reply.Text);
        Assert.Contains("not running", reply.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing was started", reply.Text, StringComparison.Ordinal);
    }

    /// <summary>A call reaches the application over the pipe and the answer comes back.</summary>
    /// <remarks>
    /// The two ends compiled into two assemblies have to agree about the wire format, so this runs
    /// the real server and the real client against each other rather than trusting that they match.
    /// </remarks>
    [Fact]
    public async Task ACallCrossesThePipeAndComesBack()
    {
        var app = new FakeApp();

        // A dispatcher on a thread that pumps. The server writes one line to the feed when it
        // starts, and the feed marshals, so Dispatcher.CurrentDispatcher on a test thread deadlocks
        // the moment it is asked to. That is the same trap DispatcherLoop was written for.
        using var loop = new DispatcherLoop();
        var feed = new ActivityFeed(loop.Dispatcher);

        using var server = new McpBridgeServer(new McpToolSurface(app), feed);
        server.Start();

        try
        {
            var client = new McpBridgeClient(connectTimeout: TimeSpan.FromSeconds(5));

            var reply = await client
                .CallAsync(new McpBridgeRequest("localnexus_open_graph", Call("x", """{"name":"Minimal"}""").Arguments), CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(15));

            Assert.True(reply.Ok, reply.Text);
            Assert.Equal("Minimal", app.GraphOpened);
            Assert.Contains("Opened Minimal", reply.Text, StringComparison.Ordinal);
        }
        finally
        {
            server.Stop();
        }
    }

    /// <summary>The pipe is scoped to the account, not the machine.</summary>
    [Fact]
    public void ThePipeIsPerUser()
        => Assert.Contains(Environment.UserName, McpBridge.PipeName, StringComparison.Ordinal);
}
