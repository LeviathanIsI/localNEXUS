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
/// Two files of one plan declaring the same name.
/// </summary>
/// <remarks>
/// The duplicate guard runs while a plan is being made and sees a path and one type name per row.
/// It cannot see what a file turns out to declare, because that is decided by the coder afterwards,
/// and one got through exactly there: a plan created ItemStack.cs and Inventory.cs, and Inventory
/// was written with an ItemStack nested inside it. The project ended with two types called
/// ItemStack, it compiled, and nothing said a word.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class DeclaredTwiceTests
{
    private const string Stack = """
        namespace Game
        {
            public class ItemStack
            {
                public int Count;
            }
        }
        """;

    /// <summary>Inventory with an ItemStack nested inside it, which is what the coder produced.</summary>
    private const string InventoryWithNestedStack = """
        namespace Game
        {
            public class Inventory
            {
                public class ItemStack
                {
                    public int Count;
                }
            }
        }
        """;

    private const string InventoryAlone = """
        namespace Game
        {
            public class Inventory
            {
                public int Slots;
            }
        }
        """;

    private static async Task<IReadOnlyList<IndexedType>> TypesIn(SampleProject project, string fileName, string content)
    {
        var path = project.Write(fileName, content);
        var parsed = SourceFileParser.Parse(path, "Assets/Scripts/" + fileName, CancellationToken.None);

        Assert.NotNull(parsed);

        System.IO.File.Delete(path);
        return parsed.Types;
    }

    private static GeneratedFile File(string path, string typeName, string content, IReadOnlyList<IndexedType> types)
        => new(
            new CodeTask(1, "Assets/Scripts/" + path, typeName, FileOperation.Create, "for the test", string.Empty, null),
            content,
            types)
        {
            Check = FileCheckState.Compiled
        };

    private static async Task<(RunContext Run, SampleProject Project)> WritePlan(
        TestServices services,
        SampleProject project,
        params GeneratedFile[] plan)
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

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        return (run, project);
    }

    /// <summary>
    /// A nested type colliding with a sibling file is refused, and the sibling is kept.
    /// </summary>
    /// <remarks>
    /// The case, verbatim. Both files compile, so nothing downstream would have noticed: it is not
    /// an error, it is a project with two of something.
    /// </remarks>
    [Fact]
    public async Task AFileDeclaringANameAnotherFileAlreadyDeclaredIsRefused()
    {
        using var services = TestServices.Create();
        using var project = SampleProject.Create();

        var stackTypes = await TypesIn(project, "ItemStack.cs", Stack);
        var inventoryTypes = await TypesIn(project, "Inventory.cs", InventoryWithNestedStack);

        var (run, _) = await WritePlan(
            services,
            project,
            File("ItemStack.cs", "ItemStack", Stack, stackTypes),
            File("Inventory.cs", "Inventory", InventoryWithNestedStack, inventoryTypes));

        // Unresolved rather than Completed, which is right: a run that kept something back has
        // not failed and has not finished, and v1.12 gave that its own state for this reason.
        Assert.Equal(RunState.Unresolved, run.State);

        // The first one lands, because the plan's own order decides which survives.
        Assert.True(project.Exists("ItemStack.cs"));
        Assert.False(project.Exists("Inventory.cs"));

        var refusal = Assert.Single(run.Decisions, d => d.Kind == RunDecisionKind.WriteRefused);

        Assert.Equal(nameof(ProjectWriteRule.NothingDeclaredTwice), refusal.Rule);
        Assert.Contains("ItemStack", refusal.Detail, StringComparison.Ordinal);
        Assert.Contains("Inventory.cs", refusal.Detail, StringComparison.Ordinal);
    }

    /// <summary>A plan whose files declare different names is written whole.</summary>
    /// <remarks>
    /// The half that says this is not simply refusing multi file plans. Nothing about an ordinary
    /// plan changes.
    /// </remarks>
    [Fact]
    public async Task APlanDeclaringDifferentNamesIsWrittenWhole()
    {
        using var services = TestServices.Create();
        using var project = SampleProject.Create();

        var stackTypes = await TypesIn(project, "ItemStack.cs", Stack);
        var inventoryTypes = await TypesIn(project, "Inventory.cs", InventoryAlone);

        var (run, _) = await WritePlan(
            services,
            project,
            File("ItemStack.cs", "ItemStack", Stack, stackTypes),
            File("Inventory.cs", "Inventory", InventoryAlone, inventoryTypes));

        Assert.Equal(RunState.Completed, run.State);
        Assert.True(project.Exists("ItemStack.cs"));
        Assert.True(project.Exists("Inventory.cs"));
        Assert.DoesNotContain(run.Decisions, d => d.Kind == RunDecisionKind.WriteRefused);
    }

    /// <summary>The refusal names the rule, so it is countable rather than a sentence.</summary>
    [Fact]
    public void TheRuleHasAName()
        => Assert.Equal("NothingDeclaredTwice", ProjectWriteRule.NothingDeclaredTwice.ToString());
}
