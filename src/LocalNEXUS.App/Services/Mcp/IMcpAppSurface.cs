namespace LocalNEXUS.App.Services.Mcp;

/// <summary>What a run has got to, as far as a caller needs to know.</summary>
/// <param name="RunId">The identifier the history files it under, or null when nothing recorded it.</param>
/// <param name="State">Idle, Running, Paused, Completed, Unresolved or Faulted.</param>
/// <param name="IsFinished">False while it is still going, which is what a poller watches.</param>
public sealed record McpRunHandle(string? RunId, string State, bool IsFinished);

/// <summary>
/// Everything the MCP tools are allowed to ask the application to do.
/// </summary>
/// <remarks>
/// An interface rather than a reference to the view model, and the reason is the list itself. What
/// a caller over a pipe can cause is exactly these eight things, which is a sentence somebody can
/// check; a reference to the view model would make it whatever that class happens to expose next
/// month.
///
/// Two things are deliberately not here and cannot be added without this file changing. There is no
/// way to write a file: the output node writes, inside the project boundary and through the
/// guardrails, and that stays the only path. And there is no way to read a credential, because the
/// store exists so that keys stay out of everything and a tool surface is everything.
/// </remarks>
public interface IMcpAppSurface
{
    /// <summary>Opens a project folder and reports what it turned out to be.</summary>
    /// <exception cref="System.IO.DirectoryNotFoundException">The folder is not there.</exception>
    Task<string> OpenProjectAsync(string path, CancellationToken ct);

    /// <summary>What is open now: project, kind, graph, and whether a run is going.</summary>
    Task<string> DescribeStateAsync(CancellationToken ct);

    /// <summary>The graphs that can be opened by name, saved ones and the templates that ship.</summary>
    Task<IReadOnlyList<string>> ListGraphsAsync(CancellationToken ct);

    /// <summary>Opens one by name and reports what it holds.</summary>
    /// <exception cref="System.IO.FileNotFoundException">Nothing of that name.</exception>
    Task<string> OpenGraphAsync(string name, CancellationToken ct);

    /// <summary>
    /// Starts a run of the open graph and returns as soon as it has begun.
    /// </summary>
    /// <remarks>
    /// Starts rather than finishes, and the reason is that a graph against a local model takes
    /// seconds to minutes. An MCP call that blocks for minutes is a call many clients give up on,
    /// and a client that gives up leaves the run going with nobody able to read what it did. A
    /// handle is the honest shape for something long: the caller is told it began, and asks.
    /// </remarks>
    /// <exception cref="System.InvalidOperationException">Nothing to run, or a run is already going.</exception>
    Task<McpRunHandle> StartRunAsync(string request, CancellationToken ct);

    /// <summary>Where the current or most recent run has got to.</summary>
    Task<McpRunHandle> RunStateAsync(CancellationToken ct);

    /// <summary>Everything the most recent run did, once it has finished.</summary>
    Task<string> DescribeRunAsync(string? runId, CancellationToken ct);

    /// <summary>The most recent runs, newest first.</summary>
    Task<string> ListRunsAsync(int limit, CancellationToken ct);
}
