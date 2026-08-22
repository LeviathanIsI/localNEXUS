using LocalNEXUS.App.Services.Debate;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// What the index calls a type that is declared inside another one.
/// </summary>
/// <remarks>
/// It used to call it by its namespace and its own name, so a class ItemStack inside a class
/// Inventory was recorded as Game.ItemStack. That is the name of a different type, which may or may
/// not exist, and three things trust it: the duplicate guard asks whether the project already holds
/// a name, the elicitation check asks whether a request names anything real, and the convergence
/// meter counts a name only when the project has it. A wrong name is a wrong answer in all three,
/// silently.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class NestedTypeIndexTests
{
    /// <summary>Parses a snippet the way the index parses a file, which is from disk.</summary>
    private static IReadOnlyList<IndexedType> TypesIn(string content)
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N") + ".cs");

        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, content);

        try
        {
            var parsed = SourceFileParser.Parse(path, "Assets/Scripts/Whatever.cs", CancellationToken.None);
            Assert.NotNull(parsed);
            return parsed.Types;
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    private static IndexedType Named(IReadOnlyList<IndexedType> types, string name)
        => Assert.Single(types, t => t.Name == name);

    /// <summary>A top level type is what it always was.</summary>
    [Fact]
    public void ATopLevelTypeIsUnchanged()
    {
        var types = TypesIn("""
            namespace Game
            {
                public class Inventory
                {
                }
            }
            """);

        var inventory = Named(types, "Inventory");

        Assert.Equal("Game.Inventory", inventory.FullName);
        Assert.Equal("Game", inventory.Namespace);
        Assert.Equal(string.Empty, inventory.ContainingTypes);
    }

    /// <summary>A nested type carries the type it is declared inside.</summary>
    [Fact]
    public void ANestedTypeCarriesItsContainingType()
    {
        var types = TypesIn("""
            namespace Game
            {
                public class Inventory
                {
                    public class ItemStack
                    {
                    }
                }
            }
            """);

        var stack = Named(types, "ItemStack");

        Assert.Equal("Game.Inventory.ItemStack", stack.FullName);
        Assert.Equal("Inventory", stack.ContainingTypes);

        // The short name is untouched, which is what the duplicate guard matches on.
        Assert.Equal("ItemStack", stack.Name);
    }

    /// <summary>Two levels deep is right too, because one level is a rule that is wrong at two.</summary>
    [Fact]
    public void ATypeNestedTwoLevelsDeepIsCorrect()
    {
        var types = TypesIn("""
            namespace Game
            {
                public class Inventory
                {
                    public class Slot
                    {
                        public enum Kind
                        {
                            Empty
                        }
                    }
                }
            }
            """);

        Assert.Equal("Game.Inventory.Slot.Kind", Named(types, "Kind").FullName);
        Assert.Equal("Inventory.Slot", Named(types, "Kind").ContainingTypes);
    }

    /// <summary>A type in the global namespace nested in another has no stray leading dot.</summary>
    [Fact]
    public void ANestedTypeWithNoNamespaceReadsCleanly()
    {
        var types = TypesIn("""
            public class Outer
            {
                public class Inner
                {
                }
            }
            """);

        Assert.Equal("Outer.Inner", Named(types, "Inner").FullName);
        Assert.Equal("Outer", Named(types, "Outer").FullName);
    }

    /// <summary>A file scoped namespace means the same as a braced one.</summary>
    [Fact]
    public void AFileScopedNamespaceIsTheSameAsABracedOne()
    {
        var types = TypesIn("""
            namespace Game;

            public class Inventory
            {
                public class ItemStack
                {
                }
            }
            """);

        Assert.Equal("Game.Inventory.ItemStack", Named(types, "ItemStack").FullName);
    }

    /// <summary>
    /// A namespace nested inside another carries both.
    /// </summary>
    /// <remarks>
    /// Found while fixing the types. It reported only the innermost, so a type in Game containing
    /// Inventory came back as Inventory.Slot, which names nothing.
    /// </remarks>
    [Fact]
    public void ANamespaceNestedInsideAnotherCarriesBoth()
    {
        var types = TypesIn("""
            namespace Game
            {
                namespace Inventory
                {
                    public class Slot
                    {
                    }
                }
            }
            """);

        var slot = Named(types, "Slot");

        Assert.Equal("Game.Inventory", slot.Namespace);
        Assert.Equal("Game.Inventory.Slot", slot.FullName);
    }

    /// <summary>
    /// A generic type is indexed under its bare name, without arity.
    /// </summary>
    /// <remarks>
    /// Recorded rather than fixed, because it is a loss of fidelity that the things reading this
    /// actually want. Cache and Cache of T read as one name to a person, and the duplicate guard is
    /// there to stop a project acquiring two of something a person would call by one name. Putting
    /// arity in the full name would also stop the elicitation check matching a request that says
    /// Cache against a type declared as Cache of T, which is how anybody would write it.
    ///
    /// What it costs: two generic types of the same name and different arity are legitimately
    /// distinct in C# and are one name here, so the guard would refuse the second. That is the
    /// safer direction to be wrong in, and it is a refusal that says what it is refusing.
    /// </remarks>
    [Fact]
    public void AGenericTypeIsIndexedWithoutItsArity()
    {
        var types = TypesIn("""
            namespace Game
            {
                public class Cache<TKey, TValue>
                {
                }
            }
            """);

        var cache = Named(types, "Cache");

        Assert.Equal("Game.Cache", cache.FullName);
    }

    /// <summary>
    /// A partial type declared in two files is two entries, and both say so.
    /// </summary>
    /// <remarks>
    /// Checked rather than changed. Two entries is what is actually there, and every consumer that
    /// could mistake it for a duplicate already asks whether the declarations are partial before
    /// refusing anything.
    /// </remarks>
    [Fact]
    public void APartialTypeIsRecordedAsPartial()
    {
        var types = TypesIn("""
            namespace Game
            {
                public partial class Inventory
                {
                }
            }
            """);

        Assert.True(Named(types, "Inventory").IsPartial);
    }
}

/// <summary>
/// The three things that trust the index, checked against a nested type.
/// </summary>
/// <remarks>
/// The defect was never visible in the index on its own. It mattered because these three ask it
/// questions and believe the answers.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class NestedTypeConsumerTests
{
    private static async Task<ProjectIndexService> IndexWith(SampleProject project, string fileName, string content)
    {
        project.Write(fileName, content);

        var index = new ProjectIndexService();
        await index.EnsureAsync(project.Root, null, CancellationToken.None);
        return index;
    }

    private const string NestedFile = """
        namespace Game
        {
            public class Loadout
            {
                public class Slot
                {
                    public string ItemId;
                }
            }
        }
        """;

    /// <summary>The duplicate guard sees a nested type as something the project already has.</summary>
    /// <remarks>
    /// It matches on the short name, so this was already true, and the point is that it stays true
    /// now the full name has changed underneath it.
    /// </remarks>
    [Fact]
    public async Task TheDuplicateGuardSeesANestedType()
    {
        using var project = SampleProject.Create();
        var index = await IndexWith(project, "Loadout.cs", NestedFile);

        var verdict = DuplicateTypeGuard.Check(index, "Slot", "Assets/Scripts/Slot.cs", Array.Empty<string>());

        Assert.False(verdict.Allowed);
        Assert.Contains("Slot", verdict.Message, StringComparison.Ordinal);

        // And it says where the existing one lives, which is the file the nesting is in.
        Assert.Equal("Assets/Scripts/Loadout.cs", verdict.ExistingPath);
    }

    /// <summary>A request naming a nested type is a request that names something real.</summary>
    [Fact]
    public async Task TheElicitationCheckRecognisesANestedType()
    {
        using var project = SampleProject.Create();
        var index = await IndexWith(project, "Loadout.cs", NestedFile);

        Assert.True(RequestScope.NamesSomethingExisting("Give Slot a stack size.", index));
        Assert.True(RequestScope.IsPlannable("Give Slot a stack size.", index));

        // By its full name as well, which is the half that was wrong.
        Assert.True(RequestScope.NamesSomethingExisting("Change Game.Loadout.Slot please.", index));
    }

    /// <summary>The convergence meter counts a nested type, by either name.</summary>
    [Fact]
    public async Task TheConvergenceMeterCountsANestedType()
    {
        using var project = SampleProject.Create();
        var index = await IndexWith(project, "Loadout.cs", NestedFile);

        var known = index.Files
            .SelectMany(f => f.Types.SelectMany(t => new[] { t.Name, t.FullName }))
            .ToList();

        var measured = ConvergenceMeter.Measure(
            "Put stacking on Slot, keep Loadout, and leave Health alone.",
            "Put stacking on Slot, keep Loadout, and leave Health alone.",
            known.Concat(new[] { "Health" }).ToList());

        Assert.True(measured.IsMeasured);
        Assert.Contains("Slot", measured.SharedIdentifiers);
        Assert.Contains("Loadout", measured.SharedIdentifiers);
    }
}