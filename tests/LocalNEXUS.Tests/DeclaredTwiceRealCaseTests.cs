using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The two duplicates that actually reached disk, reproduced from the files that did it.
/// </summary>
/// <remarks>
/// The first test of this rule used content invented to look like the failure and passed against
/// code that does not stop the failure. These use the bytes the coder really produced, taken out of
/// the eval result, which is the difference between testing the shape of a bug and testing the bug.
///
/// Types are built the way the live path builds them, by parsing the content, rather than by being
/// handed in, so that whatever the parser makes of these files is what the guard is given.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class DeclaredTwiceRealCaseTests
{
    /// <summary>Plan row one. Declares Item, and nothing else.</summary>
    private const string ItemFile = """
        namespace Game
        {
            public class Item
            {
                public string Id;
            }
        }
        """;

    /// <summary>
    /// Plan row five, verbatim. It declares ItemDatabase, ItemData, a class called Game, and an
    /// Item nested inside that. The second Item is the duplicate.
    /// </summary>
    private const string ItemDatabaseFile = """
        using System.Collections.Generic;

        namespace Game
        {
            public class ItemDatabase
            {
                public List<ItemData> items;
            }

            public class ItemData
            {
                public string Id;
            }

            public class Game
            {
                public class Item
                {
                    public string Id;
                }
            }
        }
        """;

    /// <summary>
    /// Inventory as the coder wrote it: a second top level InventorySlot beside the one the
    /// project already has in its own file.
    /// </summary>
    private const string InventoryFile = """
        using System.Collections.Generic;

        namespace Game
        {
            public class Inventory
            {
                public List<InventorySlot> Slots = new List<InventorySlot>();
            }

            public class InventorySlot
            {
                public string ItemId;
                public int Count;
            }
        }
        """;

    /// <summary>Builds the file the way the coder does, by parsing what it wrote.</summary>
    private static GeneratedFile Generated(string relativePath, string typeName, string content)
    {
        var temporary = System.IO.Path.GetTempFileName();

        try
        {
            System.IO.File.WriteAllText(temporary, content);

            var parsed = SourceFileParser.Parse(temporary, relativePath, CancellationToken.None);
            Assert.NotNull(parsed);

            return new GeneratedFile(
                new CodeTask(1, relativePath, typeName, FileOperation.Create, "for the test", string.Empty, null),
                content,
                parsed.Types)
            {
                Check = FileCheckState.Compiled
            };
        }
        finally
        {
            System.IO.File.Delete(temporary);
        }
    }

    private static async Task<RunContext> WritePlan(TestServices services, SampleProject project, params GeneratedFile[] plan)
    {
        var graph = new GraphModel();

        var source = new PlanEmittingNode("coder", plan);
        var output = (OutputNode)services.Factory.Create("Output");

        graph.AddNode(source);
        graph.AddNode(output);
        Assert.True(graph.TryConnect(source.Out, output.Content, out _));

        services.Project.Open(project.Root);
        services.Services.Staging.OpenProject(project.Root);
        await services.Index.EnsureAsync(project.Root, null, CancellationToken.None);

        return await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);
    }

    private static IReadOnlyList<string> Refusals(RunContext run)
        => run.Decisions
            .Where(d => d.Kind == RunDecisionKind.WriteRefused && d.Rule == nameof(ProjectWriteRule.NothingDeclaredTwice))
            .Select(d => d.RelativePath)
            .ToList();

    /// <summary>
    /// What the parser makes of the file that collided, which is the whole question.
    /// </summary>
    /// <remarks>
    /// If this does not list Item, the guard was never given anything to catch and every other
    /// test of it is testing the wrong thing.
    /// </remarks>
    [Fact]
    public void TheCollidingFileIsSeenToDeclareTheDuplicate()
    {
        var file = Generated("Assets/Scripts/ItemDatabase.cs", "ItemDatabase", ItemDatabaseFile);
        var names = file.Types.Select(t => t.Name).ToList();

        Assert.Contains("ItemDatabase", names);
        Assert.Contains("Item", names);
    }

    /// <summary>Two files of one plan declaring Item: the second is refused.</summary>
    [Fact]
    public async Task TheSecondFileOfAPlanDeclaringTheSameNameIsRefused()
    {
        using var services = TestServices.Create();
        using var project = SampleProject.Create();

        var run = await WritePlan(
            services,
            project,
            Generated("Assets/Scripts/Item.cs", "Item", ItemFile),
            Generated("Assets/Scripts/ItemDatabase.cs", "ItemDatabase", ItemDatabaseFile));

        Assert.Contains("Assets/Scripts/ItemDatabase.cs", Refusals(run));
        Assert.True(project.Exists("Item.cs"));
        Assert.False(project.Exists("ItemDatabase.cs"));
    }

    /// <summary>
    /// A generated file colliding with a type the project already has is refused.
    /// </summary>
    /// <remarks>
    /// The half that was missing by construction. InventorySlot is in the seed project, in its own
    /// file, and nothing in the plan mentions it, so comparing only against the plan could never
    /// see this.
    /// </remarks>
    [Fact]
    public async Task AFileCollidingWithTheProjectIsRefused()
    {
        using var services = TestServices.Create();
        using var project = SampleProject.Create();

        Assert.True(project.Exists("InventorySlot.cs"));

        var run = await WritePlan(
            services,
            project,
            Generated("Assets/Scripts/Inventory.cs", "Inventory", InventoryFile));

        Assert.Contains("Assets/Scripts/Inventory.cs", Refusals(run));
        Assert.False(project.Exists("Inventory.cs"));
    }

    /// <summary>Editing the file a type already lives in is not a duplicate.</summary>
    /// <remarks>
    /// The obvious way to get the previous test passing is to refuse anything naming a type the
    /// project has, which would refuse every edit there is.
    /// </remarks>
    [Fact]
    public async Task EditingTheFileATypeAlreadyLivesInIsAllowed()
    {
        using var services = TestServices.Create();
        using var project = SampleProject.Create();

        var edited = """
            namespace Game
            {
                public class InventorySlot
                {
                    public string ItemId;
                    public int Count;
                    public int MaxStack;
                }
            }
            """;

        var file = Generated("Assets/Scripts/InventorySlot.cs", "InventorySlot", edited);

        var run = await WritePlan(
            services,
            project,
            file with { Task = new CodeTask(1, "Assets/Scripts/InventorySlot.cs", "InventorySlot", FileOperation.Edit, "add a cap", string.Empty, "x") });

        Assert.Empty(Refusals(run));
        Assert.Contains("MaxStack", project.Read("InventorySlot.cs"));
    }
}
