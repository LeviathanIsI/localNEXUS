using System.Text.Json.Nodes;
using LocalNEXUS.App.Services.Spec;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The reader, against what the OpenSpec CLI really prints.
/// </summary>
/// <remarks>
/// v1.43 built the host side against shapes inferred from documentation and said so. These are the
/// shapes taken from OpenSpec 1.10.0 on this machine, pasted from what the commands actually
/// printed, so the mapping is held to the real thing rather than to what it was assumed to be.
///
/// The bridge that produces them is exercised separately, by running it: it is a Node package and
/// nothing in this suite can host one. What is worth pinning here is the half that turns its answer
/// into what the tab draws.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class SpecBridgeShapeTests
{
    /// <summary>
    /// A change part way through, as the bridge sends it after reading openspec status.
    /// </summary>
    /// <remarks>
    /// The states are the CLI's own words. done, ready and blocked came back exactly like this,
    /// which is the one assumption v1.43 made that turned out to be right.
    /// </remarks>
    private const string PartWayThrough = """
        {
          "id": "add-search",
          "name": "add-search",
          "status": "active",
          "artifacts": [
            { "id": "proposal", "name": "proposal", "state": "done", "detail": "proposal.md" },
            { "id": "specs", "name": "specs", "state": "done", "detail": "specs/**/*.md" },
            { "id": "design", "name": "design", "state": "ready", "detail": "design.md" },
            { "id": "tasks", "name": "tasks", "state": "done", "detail": "tasks.md" }
          ]
        }
        """;

    /// <summary>A change with nothing written, where three artifacts are waiting on one.</summary>
    private const string NothingWritten = """
        {
          "id": "empty-change",
          "name": "empty-change",
          "status": "active",
          "artifacts": [
            { "id": "proposal", "name": "proposal", "state": "ready", "detail": "proposal.md" },
            { "id": "specs", "name": "specs", "state": "blocked", "detail": "Waiting on proposal." },
            { "id": "design", "name": "design", "state": "blocked", "detail": "Waiting on proposal." },
            { "id": "tasks", "name": "tasks", "state": "blocked", "detail": "Waiting on specs, design." }
          ]
        }
        """;

    private static SpecChange Read(string json)
        => SpecWorkerReader.ReadChange((JsonObject)JsonNode.Parse(json)!);

    /// <summary>The four artifacts OpenSpec's spec driven schema has, in its order.</summary>
    [Fact]
    public void TheFourArtifactsAreReadInOrder()
    {
        var change = Read(PartWayThrough);

        Assert.Equal(
            new[] { "proposal", "specs", "design", "tasks" },
            change.Artifacts.Select(a => a.Id).ToArray());

        Assert.Equal("add-search", change.Id);
        Assert.Equal(SpecChangeStatus.Active, change.Status);
    }

    /// <summary>
    /// Which artifact is next is read, not derived.
    /// </summary>
    /// <remarks>
    /// The case that would catch a second implementation. OpenSpec reports tasks as done while
    /// design, which tasks depends on, is only ready, because done means the file exists rather
    /// than that everything before it is finished. Anything working the order out for itself would
    /// disagree; reading the first one the CLI called ready gets it right by not trying.
    /// </remarks>
    [Fact]
    public void TheNextArtifactIsWhicheverTheCliCalledReady()
    {
        var change = Read(PartWayThrough);

        Assert.Equal("design", change.NextReady!.Id);
        Assert.Equal("3 of 4 done, next is design", change.Summary);
    }

    /// <summary>Blocked artifacts carry the dependency the CLI named.</summary>
    [Fact]
    public void BlockedArtifactsSayWhatTheyAreWaitingOn()
    {
        var change = Read(NothingWritten);

        Assert.Equal(SpecArtifactState.Ready, change.Artifacts[0].State);
        Assert.Equal("proposal", change.NextReady!.Id);

        var tasks = change.Artifacts.Single(a => a.Id == "tasks");

        Assert.Equal(SpecArtifactState.Blocked, tasks.State);
        Assert.Equal("Waiting on specs, design.", tasks.Detail);
    }

    /// <summary>
    /// An archived change arrives with no artifacts, and that is not a failure.
    /// </summary>
    /// <remarks>
    /// openspec list reports only active changes, so the bridge reads the archive folder for the
    /// rest and does not ask status about them: they sit outside the directory the CLI resolves
    /// against, and asking would report them as missing rather than as finished.
    /// </remarks>
    [Fact]
    public void AnArchivedChangeHasNoArtifactsAndHasNotFailed()
    {
        var change = Read("""
            { "id": "old-thing", "name": "old-thing", "status": "archived", "artifacts": [] }
            """);

        Assert.Equal(SpecChangeStatus.Archived, change.Status);
        Assert.Empty(change.Artifacts);
        Assert.Null(change.NextReady);
        Assert.Equal("0 of 0 done", change.Summary);
    }

    /// <summary>
    /// The status field on openspec list is about tasks, not about the change's life.
    /// </summary>
    /// <remarks>
    /// A trap worth pinning. list reports status as in-progress or no-tasks, which is how many
    /// checkboxes are ticked; the contract's status is active or archived. Anything passing the
    /// one through as the other reads every change as active, which happens to be right for the
    /// active ones and silently wrong for the rest.
    /// </remarks>
    [Fact]
    public void ATaskStatusIsNotAChangeStatus()
    {
        var change = Read("""
            { "id": "x", "name": "x", "status": "in-progress", "artifacts": [] }
            """);

        // Anything that is not the word archived is read as active, so a task status cannot make
        // a change look archived.
        Assert.Equal(SpecChangeStatus.Active, change.Status);
    }

    /// <summary>A state this build has not heard of is Unknown rather than a guess.</summary>
    [Fact]
    public void AStateFromANewerOpenSpecIsUnknown()
    {
        var change = Read("""
            {
              "id": "x", "name": "x", "status": "active",
              "artifacts": [ { "id": "review", "name": "review", "state": "awaiting-review" } ]
            }
            """);

        Assert.Equal(SpecArtifactState.Unknown, change.Artifacts[0].State);

        // And it is not offered as the next thing to write, because nothing said it was ready.
        Assert.Null(change.NextReady);
    }

    /// <summary>An artifact with no name of its own is shown by its identifier.</summary>
    /// <remarks>
    /// openspec status reports an id and an output path and no display name, so the bridge sends
    /// the id as both and this is what happens if it ever sends only one.
    /// </remarks>
    [Fact]
    public void AnArtifactWithoutANameFallsBackToItsId()
    {
        var change = Read("""
            { "id": "x", "name": "x", "status": "active",
              "artifacts": [ { "id": "design", "state": "ready" } ] }
            """);

        Assert.Equal("design", change.Artifacts[0].Name);
        Assert.Null(change.Artifacts[0].Detail);
    }
}
