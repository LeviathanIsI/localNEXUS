using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// A file compiling against what the same run produced before it.
/// </summary>
/// <remarks>
/// The claim v1.12 made and nothing has held it to since: file one is compiled alone, file two
/// with file one, and so on, so a call into a sibling generated moments earlier resolves. Without
/// it a plan of several files fails on every row that depends on another, and the repair loop
/// cannot help because the type genuinely is not there to be found.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class AccumulatedCompileTests
{
    private const string Interface = """
        namespace Game
        {
            public interface IInteractable
            {
                void Interact();
            }
        }
        """;

    private const string Implementation = """
        namespace Game
        {
            public class Door : IInteractable
            {
                private bool _open;

                public void Interact()
                {
                    _open = !_open;
                }

                public bool IsOpen => _open;
            }
        }
        """;

    private const string Broken = "namespace Game { public class Broken { public int Go() { return 1 + } } }";

    private static CodeTask Task(int order, string path, string typeName)
        => new(order, path, typeName, FileOperation.Create, "for the test", string.Empty, null);

    /// <summary>Runs a whole plan through the real check with a coder that answers from a script.</summary>
    private static async Task<(RunContext Run, CompilerCheckNode Check, IReadOnlyList<GeneratedFile> Out)> Run(
        TestServices services,
        IReadOnlyList<GeneratedFile> plan,
        int retryLimit = 0)
    {
        var graph = new GraphModel();

        var source = new PlanEmittingNode("coder", plan);
        var check = new CompilerCheckNode
        {
            RetryLimit = retryLimit,
            FailureBehaviour = CompileFailureBehaviour.ContinueWithWarning
        };

        graph.AddNode(source);
        graph.AddNode(check);
        Assert.True(graph.TryConnect(source.Out, check.Code, out _));

        var run = await new GraphExecutor(services.Services).RunAsync(graph, "go", CancellationToken.None);

        Assert.True(run.TryGetValue(check.Checked, out var emitted));

        return (run, check, emitted as IReadOnlyList<GeneratedFile> ?? Array.Empty<GeneratedFile>());
    }

    /// <summary>
    /// The second file of a plan compiles against the first.
    /// </summary>
    /// <remarks>
    /// The whole reason the accumulated set exists. Compiled alone, Door is a class implementing
    /// an interface nothing declares.
    /// </remarks>
    [Fact]
    public async Task AFileCompilesAgainstOneWrittenEarlierInTheSameRun()
    {
        using var services = TestServices.Create();

        var plan = new[]
        {
            new GeneratedFile(Task(1, "Assets/Scripts/IInteractable.cs", "IInteractable"), Interface, Array.Empty<App.Services.ProjectIndex.IndexedType>()),
            new GeneratedFile(Task(2, "Assets/Scripts/Door.cs", "Door"), Implementation, Array.Empty<App.Services.ProjectIndex.IndexedType>())
        };

        var (_, _, emitted) = await Run(services, plan);

        Assert.Equal(2, emitted.Count);
        Assert.All(emitted, f => Assert.Equal(FileCheckState.Compiled, f.Check));
    }

    /// <summary>
    /// Compiling the accumulated set does not declare anything twice.
    /// </summary>
    /// <remarks>
    /// The obvious way to get the previous test passing is to throw every file at the compiler on
    /// every pass, which produces the same type declared in two sources the moment a file is
    /// checked alongside itself.
    /// </remarks>
    [Fact]
    public async Task TheAccumulatedSetDeclaresNothingTwice()
    {
        using var services = TestServices.Create();

        var plan = new[]
        {
            new GeneratedFile(Task(1, "Assets/Scripts/IInteractable.cs", "IInteractable"), Interface, Array.Empty<App.Services.ProjectIndex.IndexedType>()),
            new GeneratedFile(Task(2, "Assets/Scripts/Door.cs", "Door"), Implementation, Array.Empty<App.Services.ProjectIndex.IndexedType>())
        };

        var (_, check, emitted) = await Run(services, plan);

        Assert.All(emitted, f => Assert.DoesNotContain("CS0101", f.CheckDetail, StringComparison.Ordinal));
        Assert.DoesNotContain("CS0101", check.LastDiagnostics, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file that does not compile does not take the rest of the plan down with it.
    /// </summary>
    /// <remarks>
    /// The accumulated set is built from what came before, and a broken file in it makes every
    /// later file fail for a reason that has nothing to do with the later file. Only the file
    /// being checked is offered for repair, so a cascade like that is unrepairable by design.
    /// </remarks>
    [Fact]
    public async Task ABrokenFileDoesNotPoisonTheOnesAfterIt()
    {
        using var services = TestServices.Create();

        var plan = new[]
        {
            new GeneratedFile(Task(1, "Assets/Scripts/Broken.cs", "Broken"), Broken, Array.Empty<App.Services.ProjectIndex.IndexedType>()),
            new GeneratedFile(Task(2, "Assets/Scripts/IInteractable.cs", "IInteractable"), Interface, Array.Empty<App.Services.ProjectIndex.IndexedType>()),
            new GeneratedFile(Task(3, "Assets/Scripts/Door.cs", "Door"), Implementation, Array.Empty<App.Services.ProjectIndex.IndexedType>())
        };

        var (_, _, emitted) = await Run(services, plan);

        Assert.Equal(3, emitted.Count);
        Assert.Equal(FileCheckState.DidNotCompile, emitted[0].Check);

        // The two that are fine are fine, and say so.
        Assert.Equal(FileCheckState.Compiled, emitted[1].Check);
        Assert.Equal(FileCheckState.Compiled, emitted[2].Check);
    }

    /// <summary>
    /// A plan naming the same file twice does not declare its type twice.
    /// </summary>
    /// <remarks>
    /// Not hypothetical. A planner has produced a plan that created Health.cs and then edited it,
    /// and compiled together those are one type declared in two sources, which is a wall of
    /// CS0101 describing a problem the code does not have.
    /// </remarks>
    [Fact]
    public async Task TheSameFilePlannedTwiceIsCompiledOnce()
    {
        using var services = TestServices.Create();

        var plan = new[]
        {
            new GeneratedFile(Task(1, "Assets/Scripts/IInteractable.cs", "IInteractable"), Interface, Array.Empty<App.Services.ProjectIndex.IndexedType>()),
            new GeneratedFile(Task(2, "Assets/Scripts/IInteractable.cs", "IInteractable"), Interface, Array.Empty<App.Services.ProjectIndex.IndexedType>())
        };

        var (_, _, emitted) = await Run(services, plan);

        Assert.All(emitted, f => Assert.Equal(FileCheckState.Compiled, f.Check));
    }
}
