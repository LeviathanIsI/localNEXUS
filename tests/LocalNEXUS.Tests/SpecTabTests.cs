using LocalNEXUS.App.Models.Extensions;
using LocalNEXUS.App.Services.Extensions;
using LocalNEXUS.App.Services.Spec;
using LocalNEXUS.App.ViewModels;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The Spec tab: when it exists, what it shows, and what it refuses to work out for itself.
/// </summary>
/// <remarks>
/// The thing most worth holding is the last of those. Nothing here may decide which artifact comes
/// next or whether a change is finished, because those are the questions OpenSpec exists to answer
/// and a second implementation would drift from it. What the application does is render what it was
/// told and hand a task list to the Workspace.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class SpecTabTests
{
    private static SpecChange Change(
        string name = "add-search",
        SpecChangeStatus status = SpecChangeStatus.Active,
        params (string Id, SpecArtifactState State)[] artifacts)
        => new(
            name,
            name,
            status,
            artifacts.Select(a => new SpecArtifact(a.Id, a.Id, a.State, null)).ToList());

    /// <summary>The preset ships, is not installed by default, and says it adds a tab.</summary>
    /// <remarks>
    /// The description matters as much as the contract. Somebody installing this should know the
    /// window is about to gain a view rather than only some tools.
    /// </remarks>
    [Fact]
    public void TheOpenSpecPresetShipsAndSaysItAddsATab()
    {
        var preset = ExtensionPresets.Find("ai.fission.openspec");

        Assert.NotNull(preset);
        Assert.Equal("OpenSpec", preset!.Name);
        Assert.Contains(ExtensionContract.Spec, preset.Contracts);
        Assert.True(preset.ProvidesTab);

        Assert.Contains("tab", preset.Description, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a tab", preset.ContributionSummary, StringComparison.Ordinal);

        // Fetched on install rather than bundled, like the other two.
        Assert.Equal("npx", preset.Launch.Command);
        Assert.Contains(preset.Launch.Arguments, a => a.Contains("openspec", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Its prerequisite is Node, and the existing check can install it.</summary>
    [Fact]
    public void ItsPrerequisiteIsNodeAndIsInstallable()
    {
        var preset = ExtensionPresets.Find("ai.fission.openspec");
        var node = Assert.Single(preset!.Prerequisites);

        Assert.Equal(PrerequisiteKind.Executable, node.Kind);
        Assert.Equal("node", node.Name);

        // Installable, which is what lets the panel offer to fix it rather than only report it.
        Assert.Equal("winget", node.InstallCommand);
        Assert.NotNull(node.InstallArguments);
    }

    /// <summary>Nothing else ships with the spec contract.</summary>
    /// <remarks>
    /// This is for OpenSpec specifically rather than a capability anything may declare, and the
    /// preset list is where that would quietly stop being true.
    /// </remarks>
    [Fact]
    public void OnlyOnePresetBringsATab()
        => Assert.Single(ExtensionPresets.All.Where(p => p.ProvidesTab));

    /// <summary>With nothing installed there is no tab.</summary>
    [Fact]
    public void TheTabIsAbsentUntilTheExtensionIsInstalled()
    {
        using var services = TestServices.Create();

        var registry = new ExtensionRegistry(services.Feed);
        var view = new SpecViewModel(registry, Host(services), services.Feed, _ => { });

        Assert.Null(view.Installed());
    }

    /// <summary>Installing one that brings a tab is what makes it appear.</summary>
    [Fact]
    public void InstallingTheExtensionIsWhatMakesTheTabAppear()
    {
        using var services = TestServices.Create();

        var registry = new ExtensionRegistry(services.Feed);
        var view = new SpecViewModel(registry, Host(services), services.Feed, _ => { });

        var preset = ExtensionPresets.Find("ai.fission.openspec")!;
        var installed = new InstalledExtension(preset, ExtensionOrigin.Preset, "preset")
        {
            State = ExtensionState.Unreachable
        };

        registry.Extensions.Add(installed);

        Assert.NotNull(view.Installed());

        // And switching it off takes it away again, because a disabled extension is never started.
        installed.IsEnabled = false;
        Assert.Null(view.Installed());
    }

    /// <summary>An extension that brings tools but no tab does not bring a tab.</summary>
    [Fact]
    public void AnExtensionWithoutTheContractBringsNoTab()
    {
        using var services = TestServices.Create();

        var registry = new ExtensionRegistry(services.Feed);
        var view = new SpecViewModel(registry, Host(services), services.Feed, _ => { });

        var other = ExtensionPresets.Find("studio.anklebreaker.unity-mcp")!;

        registry.Extensions.Add(new InstalledExtension(other, ExtensionOrigin.Preset, "preset")
        {
            State = ExtensionState.Unreachable
        });

        Assert.Null(view.Installed());
    }

    /// <summary>Which artifact is next is read from the worker, never worked out.</summary>
    /// <remarks>
    /// The change here reports its artifacts out of any sensible order and with a done one after a
    /// ready one. Anything computing the answer for itself would get this wrong; reading the first
    /// one the worker called ready gets it right by not trying.
    /// </remarks>
    [Fact]
    public void TheNextArtifactIsWhicheverTheWorkerCalledReady()
    {
        var change = Change(
            artifacts: new[]
            {
                ("proposal", SpecArtifactState.Done),
                ("design", SpecArtifactState.Ready),
                ("specs", SpecArtifactState.Done),
                ("tasks", SpecArtifactState.Blocked)
            });

        Assert.Equal("design", change.NextReady!.Id);
        Assert.Equal("2 of 4 done, next is design", change.Summary);
    }

    /// <summary>A change with nothing ready has no next artifact, and that is not a failure.</summary>
    [Fact]
    public void AChangeWithNothingReadyHasNoNext()
    {
        var change = Change(
            artifacts: new[]
            {
                ("proposal", SpecArtifactState.Done),
                ("tasks", SpecArtifactState.Blocked)
            });

        Assert.Null(change.NextReady);
        Assert.Equal("1 of 2 done", change.Summary);
    }

    /// <summary>A state this build has not heard of is Unknown rather than a guess.</summary>
    /// <remarks>
    /// A worker newer than this application may report something that did not exist when this was
    /// written, and mapping it onto the nearest state would be inventing a claim about somebody's
    /// change.
    /// </remarks>
    [Theory]
    [InlineData("done", SpecArtifactState.Done)]
    [InlineData("completed", SpecArtifactState.Done)]
    [InlineData("ready", SpecArtifactState.Ready)]
    [InlineData("blocked", SpecArtifactState.Blocked)]
    [InlineData("percolating", SpecArtifactState.Unknown)]
    [InlineData(null, SpecArtifactState.Unknown)]
    public void AnUnfamiliarStateIsUnknown(string? reported, SpecArtifactState expected)
    {
        var json = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = "one",
            ["name"] = "One",
            ["status"] = "active",
            ["artifacts"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject
                {
                    ["id"] = "proposal",
                    ["state"] = reported
                }
            }
        };

        var read = SpecWorkerReader.ReadChange(json);

        Assert.Equal(expected, read.Artifacts[0].State);
    }

    /// <summary>An archived change is read as archived.</summary>
    [Fact]
    public void AnArchivedChangeIsRead()
    {
        var json = new System.Text.Json.Nodes.JsonObject
        {
            ["id"] = "old",
            ["status"] = "archived",
            ["artifacts"] = new System.Text.Json.Nodes.JsonArray()
        };

        var read = SpecWorkerReader.ReadChange(json);

        Assert.Equal(SpecChangeStatus.Archived, read.Status);

        // And it takes its identifier as its name when the worker gave no name.
        Assert.Equal("old", read.Name);
    }

    /// <summary>The task list is found by what the worker called it.</summary>
    [Fact]
    public void TheTaskListIsFoundOnTheChange()
    {
        using var services = TestServices.Create();

        var view = new SpecViewModel(new ExtensionRegistry(services.Feed), Host(services), services.Feed, _ => { })
        {
            SelectedChange = Change(
                artifacts: new[]
                {
                    ("proposal", SpecArtifactState.Done),
                    ("tasks", SpecArtifactState.Done)
                })
        };

        Assert.Equal("tasks", view.Tasks!.Id);

        // And it can be sent, because it has been written.
        Assert.True(view.SendTasksToWorkspaceCommand.CanExecute(null));
    }

    /// <summary>A task list that has not been written yet cannot be sent.</summary>
    [Fact]
    public void ATaskListThatIsNotWrittenCannotBeSent()
    {
        using var services = TestServices.Create();

        var view = new SpecViewModel(new ExtensionRegistry(services.Feed), Host(services), services.Feed, _ => { })
        {
            SelectedChange = Change(
                artifacts: new[]
                {
                    ("proposal", SpecArtifactState.Done),
                    ("tasks", SpecArtifactState.Blocked)
                })
        };

        Assert.False(view.SendTasksToWorkspaceCommand.CanExecute(null));
    }

    /// <summary>Advancing is offered only for an active change that has something ready.</summary>
    [Fact]
    public void AdvancingIsOfferedOnlyWhenThereIsSomethingToAdvanceTo()
    {
        using var services = TestServices.Create();

        var view = new SpecViewModel(new ExtensionRegistry(services.Feed), Host(services), services.Feed, _ => { });

        view.SelectedChange = Change(artifacts: new[] { ("proposal", SpecArtifactState.Ready) });
        Assert.True(view.AdvanceCommand.CanExecute(null));

        view.SelectedChange = Change(artifacts: new[] { ("proposal", SpecArtifactState.Done) });
        Assert.False(view.AdvanceCommand.CanExecute(null));

        // Nor for an archived one, whatever it says about its artifacts.
        view.SelectedChange = Change(
            status: SpecChangeStatus.Archived,
            artifacts: new[] { ("proposal", SpecArtifactState.Ready) });

        Assert.False(view.AdvanceCommand.CanExecute(null));
    }

    /// <summary>Selecting another change clears what was being read.</summary>
    [Fact]
    public void ChangingTheSelectionClearsTheArtifact()
    {
        using var services = TestServices.Create();

        var view = new SpecViewModel(new ExtensionRegistry(services.Feed), Host(services), services.Feed, _ => { })
        {
            SelectedArtifact = new SpecArtifact("proposal", "proposal", SpecArtifactState.Done, null),
            ArtifactText = "something"
        };

        view.SelectedChange = Change(artifacts: new[] { ("proposal", SpecArtifactState.Done) });

        Assert.Null(view.SelectedArtifact);
        Assert.Equal(string.Empty, view.ArtifactText);
        Assert.False(view.HasArtifact);
    }

    /// <summary>With no extension installed, asking says so rather than throwing.</summary>
    [Fact]
    public async Task WithNothingInstalledTheTabSaysSo()
    {
        using var services = TestServices.Create();

        var view = new SpecViewModel(new ExtensionRegistry(services.Feed), Host(services), services.Feed, _ => { });

        await view.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(SpecTabState.Unreachable, view.State);
        Assert.Contains("not installed", view.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private static ExtensionHost Host(TestServices services)
        => new(new App.Services.Processes.ChildProcessGroup(), services.Feed);
}
