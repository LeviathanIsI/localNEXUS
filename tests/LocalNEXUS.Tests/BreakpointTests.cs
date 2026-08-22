using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Stopping a run on a wire, reading what is passing, and changing it.
/// </summary>
/// <remarks>
/// Every node here is <see cref="RecordingNode"/>, which lives in the test assembly and is not in
/// the factory. That is the point: a breakpoint is a property of a connection, so the executor
/// holding on one has to work for a node type it has never heard of, exactly as ordering does.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class BreakpointTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N"));

    public BreakpointTests() => System.IO.Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(_folder, recursive: true);
        }
        catch (System.IO.IOException)
        {
            // A scratch folder that will not delete is not the test's problem.
        }
    }

    private string PathFor(string name) => System.IO.Path.Combine(_folder, name + GraphSerializer.FileExtension);

    /// <summary>Releases the run as soon as it is held, optionally replacing what it holds.</summary>
    private static void ReleaseWhenHeld(BreakpointService breakpoints, string? replacement = null)
    {
        breakpoints.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(BreakpointService.Current) || breakpoints.Current is not { } stop)
            {
                return;
            }

            if (replacement is not null)
            {
                stop.Text = replacement;
            }

            stop.ContinueCommand.Execute(null);
        };
    }

    /// <summary>A wire with no breakpoint does not hold anything.</summary>
    [Fact]
    public async Task AnUnmarkedWireDoesNotStop()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var first = new RecordingNode("first", log) { Append = "alpha" };
        var second = new RecordingNode("second", log);

        graph.AddNode(first);
        graph.AddNode(second);
        Assert.True(graph.TryConnect(first.Out, second.In, out _));

        using var services = TestServices.Create();
        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.False(services.Services.Breakpoints.IsHolding);
        Assert.Equal("alpha", second.Received);
    }

    /// <summary>A marked wire holds the run and says what is on it.</summary>
    [Fact]
    public async Task AMarkedWireHoldsTheRunAndShowsTheValue()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var first = new RecordingNode("first", log) { Append = "alpha" };
        var second = new RecordingNode("second", log);

        graph.AddNode(first);
        graph.AddNode(second);
        Assert.True(graph.TryConnect(first.Out, second.In, out _));

        // TryConnect reports why it refused, not what it made, so the wire is the one just added.
        graph.Connections[^1].HasBreakpoint = true;

        using var services = TestServices.Create();

        string? seen = null;
        var where = string.Empty;

        services.Services.Breakpoints.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(BreakpointService.Current) || services.Services.Breakpoints.Current is not { } stop)
            {
                return;
            }

            seen = stop.Text;
            where = stop.Where;
            stop.ContinueCommand.Execute(null);
        };

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal(RunState.Completed, run.State);
        Assert.Equal("alpha", seen);
        Assert.Equal("first.Out to second.In", where);

        // And it let go, rather than holding on to the finished run.
        Assert.False(services.Services.Breakpoints.IsHolding);
    }

    /// <summary>What is typed at the breakpoint is what the downstream node receives.</summary>
    [Fact]
    public async Task EditingAtABreakpointChangesWhatArrives()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var first = new RecordingNode("first", log) { Append = "alpha" };
        var second = new RecordingNode("second", log);

        graph.AddNode(first);
        graph.AddNode(second);
        Assert.True(graph.TryConnect(first.Out, second.In, out _));

        // TryConnect reports why it refused, not what it made, so the wire is the one just added.
        graph.Connections[^1].HasBreakpoint = true;

        using var services = TestServices.Create();
        ReleaseWhenHeld(services.Services.Breakpoints, "something else entirely");

        await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal("something else entirely", second.Received);
    }

    /// <summary>Releasing unchanged carries the original through, whatever the box holds.</summary>
    [Fact]
    public async Task ReleasingUnchangedKeepsTheOriginal()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var first = new RecordingNode("first", log) { Append = "alpha" };
        var second = new RecordingNode("second", log);

        graph.AddNode(first);
        graph.AddNode(second);
        Assert.True(graph.TryConnect(first.Out, second.In, out _));

        // TryConnect reports why it refused, not what it made, so the wire is the one just added.
        graph.Connections[^1].HasBreakpoint = true;

        using var services = TestServices.Create();

        services.Services.Breakpoints.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(BreakpointService.Current) || services.Services.Breakpoints.Current is not { } stop)
            {
                return;
            }

            stop.Text = "typed and then thought better of";
            stop.DiscardCommand.Execute(null);
        };

        await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal("alpha", second.Received);
    }

    /// <summary>
    /// Editing one branch of a fan out leaves the other branches alone.
    /// </summary>
    /// <remarks>
    /// The reason an edited value is kept per wire rather than written back over the pin. Writing
    /// it to the pin would change what every other wire out of that pin delivers, and nothing would
    /// say so.
    /// </remarks>
    [Fact]
    public async Task EditingOneBranchOfAFanOutLeavesTheOthersAlone()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var source = new RecordingNode("source", log) { Append = "alpha" };
        var edited = new RecordingNode("edited", log);
        var untouched = new RecordingNode("untouched", log);

        graph.AddNode(source);
        graph.AddNode(edited);
        graph.AddNode(untouched);

        Assert.True(graph.TryConnect(source.Out, edited.In, out _));
        graph.Connections[^1].HasBreakpoint = true;

        Assert.True(graph.TryConnect(source.Out, untouched.In, out _));

        using var services = TestServices.Create();
        ReleaseWhenHeld(services.Services.Breakpoints, "only this branch");

        await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.Equal("only this branch", edited.Received);
        Assert.Equal("alpha", untouched.Received);
    }

    /// <summary>Cancelling a held run unwinds rather than hanging on the stop.</summary>
    [Fact]
    public async Task CancellingWhileHeldUnwinds()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var first = new RecordingNode("first", log) { Append = "alpha" };
        var second = new RecordingNode("second", log);

        graph.AddNode(first);
        graph.AddNode(second);
        Assert.True(graph.TryConnect(first.Out, second.In, out _));

        // TryConnect reports why it refused, not what it made, so the wire is the one just added.
        graph.Connections[^1].HasBreakpoint = true;

        using var services = TestServices.Create();
        using var cancellation = new CancellationTokenSource();

        services.Services.Breakpoints.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(BreakpointService.Current) && services.Services.Breakpoints.IsHolding)
            {
                cancellation.Cancel();
            }
        };

        var run = await new GraphExecutor(services.Services)
            .RunAsync(graph, "go", cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(RunState.Faulted, run.State);
        Assert.False(services.Services.Breakpoints.IsHolding);
    }

    /// <summary>A value that is not text is shown and not offered for editing.</summary>
    [Fact]
    public void AListIsShownRatherThanOfferedForEditing()
    {
        var log = new List<string>();
        var graph = new GraphModel();

        var first = new RecordingNode("first", log);
        var second = new RecordingNode("second", log);

        graph.AddNode(first);
        graph.AddNode(second);
        Assert.True(graph.TryConnect(first.Out, second.In, out _));

        var stop = new BreakpointStop(graph.Connections[^1], new[] { "one", "two" });

        Assert.False(stop.IsEditable);
        Assert.NotNull(stop.ReadOnlyReason);
        Assert.Contains("a list of 2 item(s)", stop.ReadOnlyReason!, StringComparison.Ordinal);

        // It still shows what is on the wire, which is the other half of the point.
        Assert.Contains("one", stop.Text, StringComparison.Ordinal);
        Assert.Contains("two", stop.Text, StringComparison.Ordinal);
    }

    /// <summary>A breakpoint survives being saved and opened again.</summary>
    /// <remarks>
    /// A wire that quietly loses its breakpoint between sessions is a wire somebody sets again and
    /// again, so this is written on every connection rather than only on marked ones.
    /// </remarks>
    [Fact]
    public void ABreakpointSurvivesARoundTrip()
    {
        using var services = TestServices.Create();

        var graph = new GraphModel();
        var first = services.Factory.Create("Prompt");
        var second = services.Factory.Create("Reshape");

        graph.AddNode(first);
        graph.AddNode(second);

        Assert.True(graph.TryConnect(first.Outputs[0], second.Inputs[1], out _));
        graph.Connections[^1].HasBreakpoint = true;

        var serializer = new GraphSerializer(services.Factory);
        var path = PathFor("marked");
        serializer.Save(graph, path);

        var reopened = new GraphModel();
        Assert.Empty(serializer.LoadInto(reopened, path));

        Assert.True(Assert.Single(reopened.Connections).HasBreakpoint);
    }

    /// <summary>A graph saved before breakpoints existed opens with none.</summary>
    [Fact]
    public void AGraphWithNoBreakpointFieldOpensWithNone()
    {
        using var services = TestServices.Create();

        var graph = new GraphModel();
        var first = services.Factory.Create("Prompt");
        var second = services.Factory.Create("Reshape");

        graph.AddNode(first);
        graph.AddNode(second);
        Assert.True(graph.TryConnect(first.Outputs[0], second.Inputs[1], out _));

        var serializer = new GraphSerializer(services.Factory);
        var path = PathFor("older");
        serializer.Save(graph, path);

        // The field taken back out, which is what a graph saved before breakpoints existed holds.
        System.IO.File.WriteAllText(
            path,
            System.IO.File.ReadAllText(path).Replace("\"breakpoint\"", "\"notAThingYet\"", StringComparison.Ordinal));

        var reopened = new GraphModel();
        Assert.Empty(serializer.LoadInto(reopened, path));

        Assert.False(Assert.Single(reopened.Connections).HasBreakpoint);
    }
}
