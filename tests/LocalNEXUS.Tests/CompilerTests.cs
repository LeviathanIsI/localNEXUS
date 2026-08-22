using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The fence between a model writing code and that code reaching the project.
/// </summary>
/// <remarks>
/// The claim being defended is narrow and load bearing: a run that reports success has compiled.
/// These tests run the real Roslyn compiler over real sources. They do not need Unity to be
/// installed, and the one that would need it says so rather than being quietly skipped.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class CompilerTests
{
    private static RoslynUnityCompiler Compiler() => new(new UnityReferenceResolver());

    [Fact]
    public async Task CodeThatCompilesPasses()
    {
        var result = await Compiler().CompileAsync(
            "public class Ok { public int Add(int a, int b) => a + b; }",
            "Ok.cs",
            projectPath: null,
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task CodeThatDoesNotCompileFailsAndSaysWhy()
    {
        var result = await Compiler().CompileAsync(
            "public class Broken { public int Add(int a, int b) { return a + } }",
            "Broken.cs",
            projectPath: null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Errors);

        // The diagnostics have to be useful to a model, which means an identifier and a line.
        var first = result.Errors[0];
        Assert.False(string.IsNullOrWhiteSpace(first.Id));
        Assert.False(string.IsNullOrWhiteSpace(first.Message));
    }

    /// <summary>
    /// With no project open, the compiler still runs and says the references were framework only.
    /// </summary>
    /// <remarks>
    /// The settled rule is that code which cannot be checked is not code that is broken. The state
    /// is what carries that, so it is asserted directly rather than inferred from the result.
    /// </remarks>
    [Fact]
    public async Task WithNoProjectTheReferencesAreReportedAsIncomplete()
    {
        var result = await Compiler().CompileAsync(
            "public class Ok { }",
            "Ok.cs",
            projectPath: null,
            CancellationToken.None);

        Assert.NotEqual(CompileReferenceState.Complete, result.ReferenceState);
        Assert.False(string.IsNullOrWhiteSpace(result.ReferenceSummary));
    }

    /// <summary>
    /// An error that is only an error because a reference is missing is marked, not trusted.
    /// </summary>
    /// <remarks>
    /// This is the phantom error case. Without Unity's assemblies, anything touching UnityEngine
    /// produces a wall of CS0246 that has nothing to do with the code. Handing those to a model as
    /// real errors sends it rewriting working code, so they are separated from the trusted ones and
    /// the result reports itself as inconclusive rather than as a failure.
    /// </remarks>
    [Fact]
    public async Task AnErrorFromAMissingReferenceIsNotTrusted()
    {
        var result = await Compiler().CompileAsync(
            """
            using UnityEngine;

            public class Spinner : MonoBehaviour
            {
                private void Update() { }
            }
            """,
            "Spinner.cs",
            projectPath: null,
            CancellationToken.None);

        // Either Unity is installed here and this compiles, or it is not and every error is a
        // missing reference. Both are correct; what must never happen is a trusted error.
        if (result.Succeeded)
        {
            Assert.Empty(result.Errors);
            return;
        }

        Assert.NotEmpty(result.Errors);
        Assert.Empty(result.TrustedErrors);
        Assert.True(result.IsInconclusive);
    }

    /// <summary>
    /// A file that calls into a sibling compiled with it resolves.
    /// </summary>
    /// <remarks>
    /// The multi file check exists because a plan writes several files and a later one calls an
    /// earlier one. Compiled alone the call is an undefined symbol, which is a repair loop chasing
    /// an error that is not there.
    /// </remarks>
    [Fact]
    public async Task AFileResolvesAgainstItsSiblings()
    {
        var sources = new[]
        {
            new CompileSource("Greeter.cs", "public class Greeter { public string Hello() => \"hi\"; }"),
            new CompileSource("Caller.cs", "public class Caller { public string Go() => new Greeter().Hello(); }")
        };

        var result = await Compiler().CompileAsync(sources, projectPath: null, CancellationToken.None);

        Assert.True(result.Succeeded, result.FormatDiagnostics(5));
    }

    /// <summary>The same file compiled alone does not resolve, which is why the set exists.</summary>
    [Fact]
    public async Task TheSameFileAloneDoesNotResolve()
    {
        var result = await Compiler().CompileAsync(
            "public class Caller { public string Go() => new Greeter().Hello(); }",
            "Caller.cs",
            projectPath: null,
            CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// The file name is taken from what the code declares rather than from what anybody said.
    /// </summary>
    /// <remarks>
    /// A MonoBehaviour whose file name does not match its class silently refuses to attach, so the
    /// name is derived from the source and the fallback is only used when nothing is declared.
    /// </remarks>
    [Theory]
    [InlineData("public class Spinner { }", "Spinner.cs")]
    [InlineData("namespace Game { public class Health { } }", "Health.cs")]
    [InlineData("// nothing here", "fallback.cs")]
    public void TheFileNameComesFromTheCode(string source, string expected)
        => Assert.Equal(expected, RoslynUnityCompiler.DeriveFileName(source, "fallback.cs"));

    /// <summary>Compilation is cancellable, because a repair loop can be stopped mid attempt.</summary>
    [Fact]
    public async Task CompilationRespectsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Compiler().CompileAsync(
            "public class Ok { }",
            "Ok.cs",
            projectPath: null,
            cancellation.Token));
    }
}
