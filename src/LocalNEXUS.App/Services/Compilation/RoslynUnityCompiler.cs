using System.Diagnostics;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// Compiles one C# file with Roslyn against the open Unity project's real reference set.
/// </summary>
/// <remarks>
/// Chosen over invoking the Unity editor for two measured reasons. It takes single digit
/// milliseconds once warm against roughly ten seconds for a batch mode editor run, which matters
/// because a repair loop compiles several times in a row. And a batch mode editor refuses to
/// open a project the editor already has open, which is precisely the situation of anyone using
/// this tool while working in Unity.
///
/// What it gives up is real: it compiles the one file rather than the project, so it cannot see
/// another file generated in the same run and not yet compiled by Unity, and it does not run
/// whatever source generators or analyzers the project configures. What it keeps is the part
/// that catches the failures this exists to catch, because the references are the project's own
/// assemblies and the editor's own Unity API, so a misspelled member or a type that does not
/// exist is found exactly as Unity would find it.
/// </remarks>
public sealed class RoslynUnityCompiler : ICodeCompiler
{
    /// <summary>
    /// The language version Unity 2021 and later accept for game code. Compiling at a newer
    /// version would let syntax through here that Unity then rejects, which is the one failure
    /// this check must never have.
    /// </summary>
    private const LanguageVersion UnityLanguageVersion = LanguageVersion.CSharp9;

    private readonly UnityReferenceResolver _references;

    public RoslynUnityCompiler(UnityReferenceResolver references) => _references = references;

    /// <inheritdoc />
    public string Name => "Roslyn against the project's Unity references";

    /// <inheritdoc />
    public CompileReferenceSet DescribeReferences(string? projectPath) => _references.Resolve(projectPath);

    /// <inheritdoc />
    public Task<CompileResult> CompileAsync(string source, string fileName, string? projectPath, CancellationToken ct)
        => CompileAsync(new[] { new CompileSource(fileName, source) }, projectPath, ct);

    /// <inheritdoc />
    public Task<CompileResult> CompileAsync(
        IReadOnlyList<CompileSource> sources,
        string? projectPath,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var referenceSet = _references.Resolve(projectPath);

        if (!referenceSet.CanCompile)
        {
            throw new CompilerUnavailableException(referenceSet.State, referenceSet.Summary);
        }

        // Roslyn is synchronous and CPU bound. Running it on the pool keeps a long compile off
        // the thread the caller is on, which during a run is the one streaming to the feed.
        return Task.Run(() => Compile(sources, referenceSet, ct), ct);
    }

    /// <summary>
    /// The file name a piece of code should be reported under. Unity requires a script's file
    /// name to match the type it declares, so the code itself is the best answer available and
    /// there is no second setting to keep in step with the output node.
    /// </summary>
    public static string DeriveFileName(string source, string fallback)
    {
        try
        {
            var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(UnityLanguageVersion));

            var declaration = tree.GetRoot()
                .DescendantNodes()
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.BaseTypeDeclarationSyntax>()
                .FirstOrDefault();

            if (declaration is not null)
            {
                return declaration.Identifier.ValueText + ".cs";
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // Code too broken to parse still gets checked; it just gets the fallback name.
        }

        return fallback;
    }

    private static CompileResult Compile(
        IReadOnlyList<CompileSource> sources,
        CompileReferenceSet referenceSet,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        var trees = sources
            .Select(s => CSharpSyntaxTree.ParseText(
                SourceText.From(s.Source),
                new CSharpParseOptions(UnityLanguageVersion),
                path: s.FileName,
                cancellationToken: ct))
            .ToList();

        var fileName = sources.Count > 0 ? sources[^1].FileName : "Generated.cs";

        var compilation = CSharpCompilation.Create(
            "LocalNEXUS.CompileCheck",
            trees,
            referenceSet.References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,

                // Unity game code is compiled without nullable reference types unless a project
                // opts in, so enabling them here would invent warnings the project never sees.
                nullableContextOptions: NullableContextOptions.Disable));

        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream, cancellationToken: ct);

        stopwatch.Stop();

        var diagnostics = emitted.Diagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => Translate(d, fileName, referenceSet.IsPartial))
            .ToList();

        return new CompileResult(
            emitted.Success,
            diagnostics,
            stopwatch.Elapsed,
            referenceSet.State,
            referenceSet.Summary);
    }

    private static CompileDiagnostic Translate(Diagnostic diagnostic, string fileName, bool referencesArePartial)
    {
        var span = diagnostic.Location.GetLineSpan();
        var located = diagnostic.Location.IsInSource;

        return new CompileDiagnostic(
            diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => CompileSeverity.Error,
                DiagnosticSeverity.Warning => CompileSeverity.Warning,
                _ => CompileSeverity.Info
            },
            diagnostic.Id,
            located && span.Path.Length > 0 ? span.Path : fileName,
            located ? span.StartLinePosition.Line + 1 : 0,
            located ? span.StartLinePosition.Character + 1 : 0,
            diagnostic.GetMessage(),
            referencesArePartial && CompileDiagnostic.IsReferenceCode(diagnostic.Id));
    }
}
