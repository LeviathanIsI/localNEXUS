using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// The project's own source, parsed once, ready to be handed to a check as a reference.
/// </summary>
/// <remarks>
/// A reference rather than more files in the compilation being checked, and that is the whole
/// design. Adding the project's files to the check would mean an error anywhere in somebody's
/// existing code failing the compile of a file that has nothing to do with it, and the check
/// reporting the model's work as broken because the project was. As a reference the project
/// contributes its declarations and nothing else: its errors are its own, and a type it declares
/// resolves exactly as it would in a real build.
///
/// The file being written is left out, matched by the type it declares. A generated file that
/// declares Coupon and a file on disk that declares Coupon are the same type declared twice, which
/// is a wall of CS0101 describing a problem that does not exist. Matched on the type rather than on
/// the path because the path a plan writes to and the path a type currently lives in are not always
/// the same, and the collision follows the type.
///
/// What that leaves: a generated file that renames the type it declares keeps the old declaration
/// visible, so a reference to the old name still resolves and the check is that much weaker for
/// that one file. Said here rather than hidden, and it is the narrower failure of the two.
/// </remarks>
public sealed class ProjectSourceSet
{
    private readonly IReadOnlyList<ParsedFile> _files;

    private ProjectSourceSet(IReadOnlyList<ParsedFile> files, string summary)
    {
        _files = files;
        Summary = summary;
    }

    /// <summary>How many files it holds.</summary>
    public int Count => _files.Count;

    /// <summary>One sentence naming what was parsed and what it cost.</summary>
    public string Summary { get; }

    /// <summary>An empty one, for a project with no source or none that could be read.</summary>
    public static ProjectSourceSet Empty { get; } = new(Array.Empty<ParsedFile>(), "no source files");

    /// <summary>Parses a set of files into trees, recording what each declares.</summary>
    public static ProjectSourceSet Parse(
        IReadOnlyList<(string Path, string Text)> files,
        LanguageVersion language,
        CancellationToken ct)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var options = new CSharpParseOptions(language);
        var parsed = new List<ParsedFile>(files.Count);

        foreach (var (path, text) in files)
        {
            ct.ThrowIfCancellationRequested();

            var tree = CSharpSyntaxTree.ParseText(text, options, path: path, cancellationToken: ct);

            var declared = tree.GetRoot(ct)
                .DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>()
                .Select(d => d.Identifier.ValueText)
                .ToHashSet(StringComparer.Ordinal);

            parsed.Add(new ParsedFile(path, tree, declared));
        }

        watch.Stop();

        return new ProjectSourceSet(
            parsed,
            $"{parsed.Count} source file(s) parsed in {watch.Elapsed.TotalMilliseconds:0} ms");
    }

    /// <summary>
    /// The project as a reference, with anything declaring one of these type names left out.
    /// </summary>
    /// <remarks>
    /// Built per check rather than cached, because which files are excluded changes with what is
    /// being written. The parsing is what costs, and that is already done; assembling a
    /// compilation over trees that exist is cheap, and it is never emitted.
    /// </remarks>
    public MetadataReference? AsReference(
        IReadOnlyCollection<string> excludedTypes,
        IReadOnlyList<MetadataReference> references,
        LanguageVersion language)
    {
        var trees = _files
            .Where(f => !f.Declares.Overlaps(excludedTypes))
            .Select(f => f.Tree)
            .ToList();

        if (trees.Count == 0)
        {
            return null;
        }

        var compilation = CSharpCompilation.Create(
            "LocalNEXUS.OpenProject",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Disable));

        // Never emitted, which is what makes this affordable and also what makes it work on a
        // project that does not currently build. Symbols come from the declaration table, and a
        // broken method body somewhere else in the project does not stop a type being visible.
        return compilation.ToMetadataReference();
    }

    private sealed record ParsedFile(string Path, SyntaxTree Tree, HashSet<string> Declares);
}
