using System.IO;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// What a check can see outside Unity.
/// </summary>
/// <remarks>
/// Before v1.41 it saw the framework and the files of the current plan, and nothing the project
/// itself declared, so any file calling into existing code came back neither compiled nor broken.
/// The v1.40 baseline put that at 63% of files. These hold the fix to the two things that have to
/// be true: a type the project declares resolves, and a project nobody has restored still works and
/// still says it is short of something.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ProjectReferenceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N"));

    public ProjectReferenceTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));

        Write("Shop.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        Write("src/Money.cs", """
            namespace Shop
            {
                public readonly struct Money
                {
                    public Money(decimal amount) => Amount = amount;

                    public decimal Amount { get; }
                }
            }
            """);

        Write("src/Basket.cs", """
            namespace Shop
            {
                public class Basket
                {
                    public Money Total;
                }
            }
            """);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A scratch folder that will not delete is the operating system's problem.
        }
    }

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static RoslynUnityCompiler Compiler() => new(new UnityReferenceResolver());

    /// <summary>An unrestored project is read anyway, and says what it is short of.</summary>
    [Fact]
    public void AnUnrestoredProjectResolvesItsOwnSource()
    {
        var set = Compiler().DescribeReferences(_root);

        Assert.Equal(CompileReferenceState.ProjectNotRestored, set.State);
        Assert.True(set.CanCompile);

        // Still partial, so a missing type is still not trusted: it could be a package.
        Assert.True(set.IsPartial);
        Assert.Contains("not been restored", set.Summary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A file calling into a type the project declares compiles.
    /// </summary>
    /// <remarks>
    /// The whole of it. Before this, Money was invisible and the file came back inconclusive.
    /// </remarks>
    [Fact]
    public async Task AFileUsingTheProjectsOwnTypeCompiles()
    {
        var source = """
            namespace Shop
            {
                public class Receipt
                {
                    public Money Charge(Basket basket)
                    {
                        return basket.Total;
                    }
                }
            }
            """;

        var result = await Compiler().CompileAsync(source, "Receipt.cs", _root, CancellationToken.None);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.ToString())));
    }

    /// <summary>A real mistake is still a real mistake.</summary>
    [Fact]
    public async Task AGenuineErrorIsStillReported()
    {
        var source = """
            namespace Shop
            {
                public class Receipt
                {
                    public decimal Charge(Basket basket)
                    {
                        return basket.NoSuchMember;
                    }
                }
            }
            """;

        var result = await Compiler().CompileAsync(source, "Receipt.cs", _root, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);
    }

    /// <summary>
    /// Editing a file the project already has is not a duplicate definition.
    /// </summary>
    /// <remarks>
    /// The trap in handing the project over as a reference. The generated Basket and the Basket on
    /// disk are one type declared twice, and the compiler would be entirely right about a problem
    /// that does not exist.
    /// </remarks>
    [Fact]
    public async Task RewritingAnExistingFileIsNotADuplicate()
    {
        var source = """
            namespace Shop
            {
                public class Basket
                {
                    public Money Total;

                    public bool IsEmpty => Total.Amount == 0m;
                }
            }
            """;

        var result = await Compiler().CompileAsync(source, "Basket.cs", _root, CancellationToken.None);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.ToString())));
        Assert.DoesNotContain(result.Errors, e => e.Id == "CS0101");
    }

    /// <summary>Syntax a modern project uses is not reported as an error.</summary>
    /// <remarks>
    /// Unity is held to C# 9 because that is what Unity accepts. Holding an ordinary project to it
    /// would report a file scoped namespace, which its own build compiles without comment.
    /// </remarks>
    [Fact]
    public async Task ModernSyntaxIsAccepted()
    {
        var source = """
            namespace Shop.Reporting;

            public record Line(string Sku, int Quantity)
            {
                public string Describe() => $"{Quantity} x {Sku}";
            }
            """;

        var result = await Compiler().CompileAsync(source, "Line.cs", _root, CancellationToken.None);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.ToString())));
    }

    /// <summary>A project with a restore record reports itself resolved and is trusted.</summary>
    [Fact]
    public void ARestoredProjectIsNotPartial()
    {
        Write("obj/project.assets.json", """
            {
              "version": 3,
              "targets": { "net8.0": {} },
              "libraries": {},
              "packageFolders": { "C:\\packages": {} },
              "project": {}
            }
            """);

        var set = Compiler().DescribeReferences(_root);

        Assert.Equal(CompileReferenceState.ProjectResolved, set.State);
        Assert.False(set.IsPartial);
        Assert.Contains("is not there", set.Summary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The obj folder is not project source.
    /// </summary>
    /// <remarks>
    /// It holds generated copies of things the project already declares, so reading it would
    /// produce exactly the duplicate definitions this is careful to avoid everywhere else.
    /// </remarks>
    [Fact]
    public async Task GeneratedOutputIsNotReadAsSource()
    {
        Write("obj/Debug/Money.g.cs", """
            namespace Shop
            {
                public readonly struct Money
                {
                    public decimal Amount { get; }
                }
            }
            """);

        var source = """
            namespace Shop
            {
                public class Receipt
                {
                    public decimal Of(Money money) => money.Amount;
                }
            }
            """;

        var result = await Compiler().CompileAsync(source, "Receipt.cs", _root, CancellationToken.None);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// A Unity project does not take this path at all.
    /// </summary>
    /// <remarks>
    /// The guarantee that the Unity numbers cannot move because of any of this. Routing is decided
    /// by what the project is, not by whether a Unity install happened to be found, so a Unity
    /// project with no editor installed still lands on the framework floor exactly as before.
    /// </remarks>
    [Fact]
    public void AUnityProjectDoesNotTakeTheProjectPath()
    {
        using var project = SampleProject.Create();

        var set = Compiler().DescribeReferences(project.Root);

        Assert.NotEqual(CompileReferenceState.ProjectResolved, set.State);
        Assert.NotEqual(CompileReferenceState.ProjectNotRestored, set.State);
        Assert.Null(set.ProjectSources);
    }

    /// <summary>No project open is still the framework floor.</summary>
    [Fact]
    public void NoProjectIsStillTheFloor()
    {
        var set = Compiler().DescribeReferences(null);

        Assert.Equal(CompileReferenceState.FrameworkOnly, set.State);
        Assert.True(set.IsPartial);
    }
}
