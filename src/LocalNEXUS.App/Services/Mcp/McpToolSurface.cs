using System.Text.Json;

namespace LocalNEXUS.App.Services.Mcp;

/// <summary>One tool, as it is advertised to a client.</summary>
/// <param name="Name">What the client calls it.</param>
/// <param name="Description">What it does, which is the only thing a model reads before choosing it.</param>
/// <param name="Schema">Its arguments, as a JSON schema object.</param>
public sealed record McpToolDescription(string Name, string Description, string Schema);

/// <summary>
/// The tools, and what each one does with the arguments it is given.
/// </summary>
/// <remarks>
/// Deliberately the whole of the decision making, with the pipe on one side of it and the
/// application on the other, so that what a tool call can cause is decided in one readable place
/// and is testable without a pipe, a window or a model.
///
/// Every answer is text. A tool that returned a structure would be returning it to a language
/// model, which reads text; and the things worth saying about a run, that a rule refused a write
/// and why, do not survive being flattened into fields.
/// </remarks>
public sealed class McpToolSurface
{
    private readonly IMcpAppSurface _app;

    public McpToolSurface(IMcpAppSurface app) => _app = app;

    /// <summary>How many runs the history tool will list at most, whatever it is asked for.</summary>
    public const int MaximumRunLimit = 100;

    /// <summary>
    /// Every tool, in the order a caller would need them.
    /// </summary>
    /// <remarks>
    /// Named with a prefix because a client holds tools from several servers in one list and
    /// "run" belonging to nobody in particular is how a model picks the wrong one.
    /// </remarks>
    public static IReadOnlyList<McpToolDescription> Tools { get; } = new[]
    {
        new McpToolDescription(
            "localnexus_status",
            "What LocalNEXUS has open right now: the project and whether it was detected as Unity or "
            + "an ordinary C# project, the graph on the canvas, and whether a run is in progress. Ask "
            + "this first; it is the cheapest way to find out whether the rest will work.",
            """{"type":"object","properties":{},"required":[]}"""),

        new McpToolDescription(
            "localnexus_open_project",
            "Point LocalNEXUS at a codebase. Reports whether it was detected as a Unity project, in "
            + "which case the Unity write rules are in force, or an ordinary C# project, and how many "
            + "source files were indexed.",
            """
            {"type":"object","properties":{"path":{"type":"string",
             "description":"Absolute path to the project folder."}},"required":["path"]}
            """),

        new McpToolDescription(
            "localnexus_list_graphs",
            "The graphs that can be opened by name: the ones saved on this machine and the templates "
            + "that ship with the application.",
            """{"type":"object","properties":{},"required":[]}"""),

        new McpToolDescription(
            "localnexus_open_graph",
            "Open a graph by name, replacing whatever is on the canvas. Use localnexus_list_graphs "
            + "for the names.",
            """
            {"type":"object","properties":{"name":{"type":"string",
             "description":"The graph or template name."}},"required":["name"]}
            """),

        new McpToolDescription(
            "localnexus_run",
            "Run the open graph against a request, exactly as pressing Run in the window would. "
            + "Returns as soon as the run has started, because a graph against a local model takes "
            + "seconds to minutes. Poll localnexus_run_result for the outcome.",
            """
            {"type":"object","properties":{"request":{"type":"string",
             "description":"What to ask for, in a sentence, as you would type it into the box."}},
             "required":["request"]}
            """),

        new McpToolDescription(
            "localnexus_run_result",
            "Where the current or most recent run has got to, and once it has finished, everything it "
            + "did: what was planned, what was written, what was held back, what a rule refused and "
            + "why, and what it cost.",
            """
            {"type":"object","properties":{"run_id":{"type":"string",
             "description":"A run from the history. Omit for the current or most recent one."}},
             "required":[]}
            """),

        new McpToolDescription(
            "localnexus_list_runs",
            "Recent runs, newest first, without running anything.",
            """
            {"type":"object","properties":{"limit":{"type":"integer","minimum":1,"maximum":100,
             "description":"How many, at most. Defaults to 20."}},"required":[]}
            """)
    };

    /// <summary>
    /// Runs one tool call and says what happened.
    /// </summary>
    /// <remarks>
    /// Never throws. Everything a caller can get wrong, an unknown tool, a missing argument, a
    /// folder that is not there, a run that is already going, comes back as a refusal that says
    /// what was wrong, because a model reads the refusal and tries again and an exception type
    /// tells it nothing.
    /// </remarks>
    public async Task<McpBridgeReply> InvokeAsync(McpBridgeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            return request.Tool switch
            {
                "localnexus_status" => McpBridgeReply.Answer(await _app.DescribeStateAsync(ct).ConfigureAwait(false)),
                "localnexus_open_project" => await OpenProjectAsync(request, ct).ConfigureAwait(false),
                "localnexus_list_graphs" => await ListGraphsAsync(ct).ConfigureAwait(false),
                "localnexus_open_graph" => await OpenGraphAsync(request, ct).ConfigureAwait(false),
                "localnexus_run" => await RunAsync(request, ct).ConfigureAwait(false),
                "localnexus_run_result" => await ResultAsync(request, ct).ConfigureAwait(false),
                "localnexus_list_runs" => await ListRunsAsync(request, ct).ConfigureAwait(false),
                _ => McpBridgeReply.Refused(
                    $"There is no tool called '{request.Tool}'. This server offers: "
                    + string.Join(", ", Tools.Select(t => t.Name)) + ".")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Anything the application threw. The caller gets the sentence rather than a stack,
            // because the caller is a model and a stack is noise it will repeat back.
            return McpBridgeReply.Refused($"{request.Tool} failed: {ex.Message}");
        }
    }

    private async Task<McpBridgeReply> OpenProjectAsync(McpBridgeRequest request, CancellationToken ct)
    {
        if (Text(request, "path") is not { Length: > 0 } path)
        {
            return McpBridgeReply.Refused("localnexus_open_project needs a 'path' to the project folder.");
        }

        return McpBridgeReply.Answer(await _app.OpenProjectAsync(path, ct).ConfigureAwait(false));
    }

    private async Task<McpBridgeReply> ListGraphsAsync(CancellationToken ct)
    {
        var graphs = await _app.ListGraphsAsync(ct).ConfigureAwait(false);

        return McpBridgeReply.Answer(graphs.Count == 0
            ? "No graphs are saved and no templates were found."
            : string.Join(Environment.NewLine, graphs.Select(g => "- " + g)));
    }

    private async Task<McpBridgeReply> OpenGraphAsync(McpBridgeRequest request, CancellationToken ct)
    {
        if (Text(request, "name") is not { Length: > 0 } name)
        {
            return McpBridgeReply.Refused(
                "localnexus_open_graph needs a 'name'. Use localnexus_list_graphs for what there is.");
        }

        return McpBridgeReply.Answer(await _app.OpenGraphAsync(name, ct).ConfigureAwait(false));
    }

    private async Task<McpBridgeReply> RunAsync(McpBridgeRequest request, CancellationToken ct)
    {
        if (Text(request, "request") is not { Length: > 0 } text)
        {
            return McpBridgeReply.Refused("localnexus_run needs a 'request' saying what to ask for.");
        }

        var handle = await _app.StartRunAsync(text, ct).ConfigureAwait(false);

        return McpBridgeReply.Answer(
            $"Started. Run {handle.RunId ?? "(not recorded)"} is {handle.State}. "
            + "Poll localnexus_run_result; a run against a local model takes seconds to minutes.");
    }

    private async Task<McpBridgeReply> ResultAsync(McpBridgeRequest request, CancellationToken ct)
    {
        var runId = Text(request, "run_id");

        if (runId is null)
        {
            var state = await _app.RunStateAsync(ct).ConfigureAwait(false);

            if (!state.IsFinished)
            {
                return McpBridgeReply.Answer(
                    $"Still running. Run {state.RunId ?? "(not recorded)"} is {state.State}. Ask again shortly.");
            }
        }

        return McpBridgeReply.Answer(await _app.DescribeRunAsync(runId, ct).ConfigureAwait(false));
    }

    private async Task<McpBridgeReply> ListRunsAsync(McpBridgeRequest request, CancellationToken ct)
    {
        var limit = 20;

        if (Value(request, "limit") is { } element)
        {
            if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out limit))
            {
                return McpBridgeReply.Refused("localnexus_list_runs wants 'limit' as a whole number.");
            }

            if (limit < 1)
            {
                return McpBridgeReply.Refused("localnexus_list_runs wants a 'limit' of one or more.");
            }

            limit = Math.Min(limit, MaximumRunLimit);
        }

        return McpBridgeReply.Answer(await _app.ListRunsAsync(limit, ct).ConfigureAwait(false));
    }

    /// <summary>One argument, or null when it was not given.</summary>
    private static JsonElement? Value(McpBridgeRequest request, string name)
    {
        if (request.Arguments is not { ValueKind: JsonValueKind.Object } arguments)
        {
            return null;
        }

        return arguments.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? value
            : null;
    }

    /// <summary>One argument as text, or null when it was not given or was not text.</summary>
    private static string? Text(McpBridgeRequest request, string name)
    {
        if (Value(request, name) is not { ValueKind: JsonValueKind.String } value)
        {
            return null;
        }

        var text = value.GetString();

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
