using System.IO;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.History;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The record of what happened, kept per project and never in memory.
/// </summary>
/// <remarks>
/// The database lives beside the project it describes, which is what makes a history meaningful:
/// a run belongs to the code it changed. Every test here creates its own throwaway project, so
/// nothing goes near a real one, and closing the store between writing and reading is deliberate
/// rather than incidental, because it proves what is on disk rather than what is in a buffer.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class HistoryTests
{
    private static async Task<RunHistoryStore> Open(SampleProject project)
    {
        var store = new RunHistoryStore();
        await store.OpenProjectAsync(project.Root, CancellationToken.None);
        return store;
    }

    /// <summary>Closing and reopening is how a test sees what actually landed on disk.</summary>
    private static async Task Reopen(RunHistoryStore store, SampleProject project)
    {
        await store.CloseAsync();
        await store.OpenProjectAsync(project.Root, CancellationToken.None);
    }

    [Fact]
    public async Task TheDatabaseLivesBesideTheProject()
    {
        using var project = SampleProject.Create();
        await using var store = await Open(project);

        Assert.True(store.IsOpen);
        Assert.Equal(
            Path.Combine(project.Root, StagingStore.FolderName, RunHistoryStore.FileName),
            store.DatabasePath);

        Assert.True(File.Exists(store.DatabasePath));
    }

    /// <summary>With no project open there is nothing to record into, and that is said plainly.</summary>
    [Fact]
    public async Task WithNoProjectThereIsNoRecord()
    {
        await using var store = new RunHistoryStore();
        await store.OpenProjectAsync(null, CancellationToken.None);

        Assert.False(store.IsOpen);
        Assert.False(string.IsNullOrWhiteSpace(store.StatusText));
    }

    [Fact]
    public async Task ARunIsRecordedAndReadBack()
    {
        using var project = SampleProject.Create();
        await using var store = await Open(project);

        store.BeginRun("run-1", "add stacking to the inventory", "my graph", 4, 3);
        store.EndRun("run-1", "Completed", 0.02m, 3);

        await Reopen(store, project);

        var run = Assert.Single(await store.ListRunsAsync(10, CancellationToken.None));

        Assert.Equal("run-1", run.RunId);
        Assert.Equal("add stacking to the inventory", run.Request);
        Assert.Equal("Completed", run.State);
    }

    [Fact]
    public async Task TheEventsOfARunAreKeptInOrder()
    {
        using var project = SampleProject.Create();
        await using var store = await Open(project);

        store.BeginRun("run-1", "something", "graph", 2, 1);
        store.RecordEvent("run-1", Guid.NewGuid(), DateTimeOffset.UtcNow, "Info", null, "first", "detail one");
        store.RecordEvent("run-1", Guid.NewGuid(), DateTimeOffset.UtcNow.AddSeconds(1), "Info", null, "second", "detail two");
        store.EndRun("run-1", "Completed", 0m, 0);

        await Reopen(store, project);

        var events = await store.ReadEventsAsync("run-1", CancellationToken.None);

        Assert.Equal(2, events.Count);
        Assert.Equal("first", events[0].Title);
        Assert.Equal("second", events[1].Title);
    }

    [Fact]
    public async Task TheFilesARunTouchedAreKept()
    {
        using var project = SampleProject.Create();
        await using var store = await Open(project);

        store.BeginRun("run-1", "something", "graph", 2, 1);
        store.RecordFile("run-1", "Assets/Scripts/Weapon.cs", FileOutcome.Written, null);
        store.RecordFile("run-1", "Assets/Scripts/Broken.cs", FileOutcome.Staged, "did not compile");
        store.EndRun("run-1", "Unresolved", 0m, 0);

        await Reopen(store, project);

        var files = await store.ReadFilesAsync("run-1", CancellationToken.None);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.Outcome == FileOutcome.Written);
        Assert.Contains(files, f => f.Outcome == FileOutcome.Staged && f.Detail == "did not compile");
    }

    /// <summary>
    /// Runs are searchable by what was asked and by what was said.
    /// </summary>
    /// <remarks>
    /// The point of keeping the record on disk rather than in memory. A session that ended last
    /// week is exactly the one worth finding.
    ///
    /// The index is populated correctly and the rows are there; the query that reads them is what
    /// fails, and it fails silently. Written as a test of the behaviour somebody expects rather
    /// than of what currently happens, because what currently happens is the defect.
    /// </remarks>
    [Fact]
    public async Task RunsAreSearchable()
    {
        using var project = SampleProject.Create();
        await using var store = await Open(project);

        store.BeginRun("run-1", "add stacking to the inventory", "graph", 2, 1);
        store.EndRun("run-1", "Completed", 0m, 0);
        store.BeginRun("run-2", "make the camera follow the player", "graph", 2, 1);
        store.EndRun("run-2", "Completed", 0m, 0);

        await Reopen(store, project);

        var hits = await store.SearchAsync("stacking", 10, CancellationToken.None);

        Assert.Single(hits);
        Assert.Equal("run-1", hits[0].RunId);
    }

    /// <summary>A search that matches nothing returns nothing rather than everything.</summary>
    [Fact]
    public async Task ASearchThatMatchesNothingFindsNothing()
    {
        using var project = SampleProject.Create();
        await using var store = await Open(project);

        store.BeginRun("run-1", "add stacking", "graph", 2, 1);
        store.EndRun("run-1", "Completed", 0m, 0);

        await Reopen(store, project);

        Assert.Empty(await store.SearchAsync("nothinglikethis", 10, CancellationToken.None));
    }

    /// <summary>
    /// A snapshot is taken before a file is overwritten, and undoing puts it back.
    /// </summary>
    /// <remarks>
    /// The one part of the record that is not just a record. Undo is separate from discarding the
    /// request precisely so that changing your mind about the code and changing your mind about
    /// the question are two different actions.
    /// </remarks>
    [Fact]
    public async Task UndoRestoresWhatWasThereBefore()
    {
        using var project = SampleProject.Create();
        await using var store = await Open(project);

        var target = project.PathTo("Spinner.cs");
        var before = File.ReadAllText(target);

        store.BeginRun("run-1", "rewrite the spinner", "graph", 2, 1);
        store.Snapshot("run-1", target);
        store.EndRun("run-1", "Completed", 0m, 0);

        await Reopen(store, project);

        File.WriteAllText(target, "// the run rewrote this");
        Assert.NotEqual(before, File.ReadAllText(target));

        var outcome = await store.UndoAsync("run-1", CancellationToken.None);

        Assert.Equal(1, outcome.Restored);
        Assert.Empty(outcome.Failed);
        Assert.Equal(before, File.ReadAllText(target));
    }

    /// <summary>
    /// A file the run created is removed rather than restored, because there was nothing before.
    /// </summary>
    [Fact]
    public async Task UndoRemovesAFileThatWasNewlyCreated()
    {
        using var project = SampleProject.Create();
        await using var store = await Open(project);

        var target = project.PathTo("BrandNew.cs");

        store.BeginRun("run-1", "write a new file", "graph", 2, 1);
        store.Snapshot("run-1", target);
        store.EndRun("run-1", "Completed", 0m, 0);

        await Reopen(store, project);

        File.WriteAllText(target, "public class BrandNew { }");

        var outcome = await store.UndoAsync("run-1", CancellationToken.None);

        Assert.Equal(1, outcome.Removed);
        Assert.False(File.Exists(target));
    }

    /// <summary>Usage is reportable, because the caps are only meaningful against a number.</summary>
    [Fact]
    public async Task UsageIsReportable()
    {
        using var project = SampleProject.Create();
        await using var store = await Open(project);

        store.BeginRun("run-1", "something", "graph", 2, 1);
        store.Snapshot("run-1", project.PathTo("Spinner.cs"));
        store.EndRun("run-1", "Completed", 0m, 0);

        await Reopen(store, project);

        var usage = await store.ReadUsageAsync(CancellationToken.None);

        Assert.Equal(1, usage.Runs);
        Assert.Equal(1, usage.Snapshots);
        Assert.True(usage.DatabaseBytes > 0);
    }

    /// <summary>Clearing the history leaves the database open and empty rather than removed.</summary>
    [Fact]
    public async Task ClearingLeavesAnEmptyRecord()
    {
        using var project = SampleProject.Create();
        await using var store = await Open(project);

        store.BeginRun("run-1", "something", "graph", 2, 1);
        store.EndRun("run-1", "Completed", 0m, 0);
        store.ClearHistory();

        await Reopen(store, project);

        Assert.True(store.IsOpen);
        Assert.Empty(await store.ListRunsAsync(10, CancellationToken.None));
    }
}

/// <summary>
/// Work a run could not finish, held so it is not thrown away.
/// </summary>
/// <remarks>
/// Per file rather than all or nothing. The earlier design discarded four working files because a
/// fifth did not build, which is the worst possible outcome: the run failed and the work went with
/// it.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class StagingTests
{
    private static StagedFile File(string path, StagedReason reason = StagedReason.DidNotCompile)
        => new(path, "Weapon", true, "add a weapon", "public class Weapon { }", reason, "CS1002", DateTimeOffset.UtcNow);

    [Fact]
    public void StagedWorkIsHeldAndCounted()
    {
        using var loop = new DispatcherLoop();
        var store = new StagingStore(loop.Dispatcher);

        store.Stage(File("Assets/Scripts/Weapon.cs"));

        Assert.True(store.HasPending);
        Assert.Equal(1, store.Count);
        Assert.False(string.IsNullOrWhiteSpace(store.Summary));
    }

    /// <summary>Staging the same path twice replaces rather than accumulating.</summary>
    [Fact]
    public void TheSameFileStagedTwiceIsOneEntry()
    {
        using var loop = new DispatcherLoop();
        var store = new StagingStore(loop.Dispatcher);

        store.Stage(File("Assets/Scripts/Weapon.cs"));
        store.Stage(File("Assets/Scripts/Weapon.cs"));

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void ResolvingForgetsOneFile()
    {
        using var loop = new DispatcherLoop();
        var store = new StagingStore(loop.Dispatcher);

        store.Stage(File("Assets/Scripts/Weapon.cs"));
        store.Stage(File("Assets/Scripts/Armour.cs"));
        store.Resolve("Assets/Scripts/Weapon.cs");

        Assert.Equal(1, store.Count);
        Assert.Equal("Assets/Scripts/Armour.cs", store.Pending[0].RelativePath);
    }

    /// <summary>
    /// The staged work as text is the intent and the errors, not the code.
    /// </summary>
    /// <remarks>
    /// What a later run needs is what this file was meant to do and what stopped it. The content
    /// is already reachable by anything that goes looking for it.
    /// </remarks>
    [Fact]
    public void TheDescriptionSaysWhatWasMeantAndWhatStoppedIt()
    {
        using var loop = new DispatcherLoop();
        var store = new StagingStore(loop.Dispatcher);

        store.Stage(File("Assets/Scripts/Weapon.cs"));

        var described = store.Describe();

        Assert.Contains("Assets/Scripts/Weapon.cs", described, StringComparison.Ordinal);
        Assert.Contains("add a weapon", described, StringComparison.Ordinal);
        Assert.Contains("CS1002", described, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingStagedDescribesAsNothing()
    {
        using var loop = new DispatcherLoop();

        Assert.Equal(string.Empty, new StagingStore(loop.Dispatcher).Describe());
    }

    /// <summary>Each reason reads as itself, because a refusal is not a compile failure.</summary>
    [Theory]
    [InlineData(StagedReason.DidNotCompile)]
    [InlineData(StagedReason.RefusedByProjectRules)]
    [InlineData(StagedReason.WriteFailed)]
    public void EveryReasonReadsAsItself(StagedReason reason)
    {
        var staged = File("Assets/Scripts/Weapon.cs", reason);

        Assert.False(string.IsNullOrWhiteSpace(staged.Summary));
        Assert.False(string.IsNullOrWhiteSpace(staged.ReasonText));
    }

    /// <summary>
    /// Staged work belongs to the project it came from and does not follow to another.
    /// </summary>
    /// <remarks>
    /// Work belonging to a project nobody has open is not work anyone can act on, and showing it
    /// invites acting on the wrong project.
    /// </remarks>
    [Fact]
    public void StagedWorkDoesNotFollowToAnotherProject()
    {
        using var loop = new DispatcherLoop();
        using var first = SampleProject.Create();
        using var second = SampleProject.Create();

        var store = new StagingStore(loop.Dispatcher);

        store.OpenProject(first.Root);
        store.Stage(File("Assets/Scripts/Weapon.cs"));

        store.OpenProject(second.Root);

        Assert.Equal(0, store.Count);
    }
}
