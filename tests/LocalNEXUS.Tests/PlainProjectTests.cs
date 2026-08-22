using System.IO;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// What happens in a codebase that is not a Unity project.
/// </summary>
/// <remarks>
/// Three things used to go wrong and each of them alone was enough to make the application worse
/// than useless outside Unity: the index read nothing because it only ever looked under Assets, the
/// write rules demanded Unity attributes from a project with no Unity in it, and the planner was
/// told it was working on a Unity project.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class PlainProjectTests
{
    /// <summary>An ordinary C# codebase, laid out the way one is.</summary>
    private sealed class PlainProject : IDisposable
    {
        private PlainProject(string root) => Root = root;

        public string Root { get; }

        public static PlainProject Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N"));
            var project = new PlainProject(root);

            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

            project.Write("src/Basket.cs", """
                namespace Shop
                {
                    public class Basket
                    {
                        public int Total;
                    }
                }
                """);

            project.Write("src/Checkout/Payment.cs", """
                namespace Shop.Checkout
                {
                    public class Payment
                    {
                        public string Reference;
                    }
                }
                """);

            // Build output, which must not be read back as project source.
            project.Write("obj/Debug/Basket.g.cs", "namespace Shop { public class Generated { } }");
            project.Write("bin/Release/Leftover.cs", "namespace Shop { public class Leftover { } }");

            // A dependency folder named by the gitignore rather than by the built in list.
            project.Write(".gitignore", "obj\nbin\nthird_party/\n# a comment\n*.user\n!keep.cs\n");
            project.Write("third_party/Vendored.cs", "namespace Other { public class Vendored { } }");

            return project;
        }

        public void Write(string relative, string content)
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // The operating system's problem, not the test's.
            }
        }
    }

    /// <summary>
    /// The index reads the project rather than an Assets folder that does not exist.
    /// </summary>
    /// <remarks>
    /// This was not a degraded experience before, it was a broken one: zero files indexed means
    /// Triage refuses, the duplicate guard has nothing to compare against, and the elicitation
    /// check decides a request naming a real type names nothing.
    /// </remarks>
    [Fact]
    public async Task TheIndexReadsAProjectWithNoAssetsFolder()
    {
        using var project = PlainProject.Create();

        var index = new ProjectIndexService();
        await index.EnsureAsync(project.Root, null, CancellationToken.None);

        Assert.Equal(ProjectIndexState.Ready, index.State);

        Assert.Single(index.FindType("Basket"));
        Assert.Single(index.FindType("Payment"));

        // Nested folders are reached, and the namespace comes back whole.
        Assert.Equal("Shop.Checkout.Payment", index.FindType("Payment")[0].FullName);
    }

    /// <summary>Build output is not project source and is not offered as though it were.</summary>
    [Fact]
    public async Task BuildOutputIsSkipped()
    {
        using var project = PlainProject.Create();

        var index = new ProjectIndexService();
        await index.EnsureAsync(project.Root, null, CancellationToken.None);

        Assert.Empty(index.FindType("Generated"));
        Assert.Empty(index.FindType("Leftover"));
    }

    /// <summary>A folder the project's own gitignore names is skipped too.</summary>
    /// <remarks>
    /// The cheap reading of a gitignore: a plain folder name and nothing else. The wildcard and the
    /// negation in the same file are skipped rather than half interpreted.
    /// </remarks>
    [Fact]
    public async Task AFolderTheGitignoreNamesIsSkipped()
    {
        using var project = PlainProject.Create();

        var index = new ProjectIndexService();
        await index.EnsureAsync(project.Root, null, CancellationToken.None);

        Assert.Empty(index.FindType("Vendored"));
    }

    /// <summary>
    /// Renaming a public field is a rename, not a lost serialized value.
    /// </summary>
    /// <remarks>
    /// The worst of the refusals to leak. Any public instance field counts as serialized, because
    /// in Unity it is, so a plain project renaming one would have been refused until it carried a
    /// [FormerlySerializedAs] attribute from a package it does not reference.
    /// </remarks>
    [Fact]
    public async Task RenamingAPublicFieldIsNotRefused()
    {
        using var project = PlainProject.Create();

        var index = new ProjectIndexService();
        await index.EnsureAsync(project.Root, null, CancellationToken.None);

        var existing = index.FindFile("src/Basket.cs");
        Assert.NotNull(existing);

        var renamed = """
            namespace Shop
            {
                public class Basket
                {
                    public int GrandTotal;
                }
            }
            """;

        // Unity would refuse this, correctly, and does below.
        Assert.Throws<UnityScriptRuleException>(
            () => UnityScriptRules.Enforce("src/Basket.cs", renamed, existing, TypesIn(renamed)));

        // And the project is not a Unity project, which is what stops the rule running at all.
        Assert.Equal(ProjectKind.Plain, ProjectService.Detect(project.Root));
    }

    /// <summary>The same edit in a Unity project is still refused.</summary>
    [Fact]
    public async Task TheSameRenameIsStillRefusedInAUnityProject()
    {
        using var project = SampleProject.Create();

        var index = new ProjectIndexService();
        await index.EnsureAsync(project.Root, null, CancellationToken.None);

        Assert.Equal(ProjectKind.Unity, ProjectService.Detect(project.Root));

        var existing = index.FindFile("Assets/Scripts/InventorySlot.cs");
        Assert.NotNull(existing);

        var renamed = """
            namespace Game
            {
                public class InventorySlot
                {
                    public string ItemIdentifier;
                    public int Count;
                }
            }
            """;

        var refusal = Assert.Throws<UnityScriptRuleException>(
            () => UnityScriptRules.Enforce("Assets/Scripts/InventorySlot.cs", renamed, existing, TypesIn(renamed)));

        Assert.Equal(ProjectWriteRule.SerializedFieldMayNotBeRenamed, refusal.Rule);
    }

    /// <summary>The planner is not told about Unity when there is no Unity.</summary>
    [Fact]
    public void ThePlannerIsNotToldAboutUnity()
    {
        var budget = new ContextBudget();

        var plain = PlanPrompt.BuildPlannerMessage("Add a discount.", "class Shop.Basket", string.Empty, budget, ProjectKind.Plain);

        // Ordinal and case sensitive on purpose. The word "opportunity" is in the last line of the
        // prompt and contains the letters, which is the sort of thing a loose check quietly passes
        // on and a strict one catches.
        Assert.DoesNotContain("Unity", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("MonoBehaviour", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Assets/Scripts", plain, StringComparison.Ordinal);

        Assert.DoesNotContain("Unity", PlanPrompt.PlannerSystemPromptFor(ProjectKind.Plain), StringComparison.Ordinal);
    }

    /// <summary>
    /// The Unity prompt is unchanged, to the byte.
    /// </summary>
    /// <remarks>
    /// The evaluation runs against a Unity shaped project and every number it produces depends on
    /// this text. The worked example in it is load bearing, and the last change to it cost one task
    /// seven runs out of ten.
    /// </remarks>
    [Fact]
    public void TheUnityPromptStillSaysWhatItSaid()
    {
        var budget = new ContextBudget();
        var unity = PlanPrompt.BuildPlannerMessage("Add a spinner.", "class Game.Spinner", string.Empty, budget, ProjectKind.Unity);

        Assert.Contains("This Unity project already contains the following.", unity, StringComparison.Ordinal);
        Assert.Contains("A MonoBehaviour file name must match its class name exactly.", unity, StringComparison.Ordinal);
        Assert.Contains(
            "Assets/Scripts/Thermostat.cs | EDIT | the target temperature lives on this type",
            unity,
            StringComparison.Ordinal);
        Assert.Contains(
            "1 | EDIT | Assets/Scripts/Thermostat.cs | Thermostat | clamp the target temperature to the safe range",
            unity,
            StringComparison.Ordinal);

        Assert.Equal(
            "You plan changes to an existing Unity project. You never write code. "
            + "You answer only in the two sections you are asked for, using the exact row format given, "
            + "with no commentary, no explanation and no markdown fences.",
            PlanPrompt.PlannerSystemPromptFor(ProjectKind.Unity));
    }

    /// <summary>Parses content the way the write path does, by parsing what was produced.</summary>
    private static IReadOnlyList<IndexedType> TypesIn(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N") + ".cs");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        try
        {
            var parsed = SourceFileParser.Parse(path, "x.cs", CancellationToken.None);
            Assert.NotNull(parsed);
            return parsed.Types;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
