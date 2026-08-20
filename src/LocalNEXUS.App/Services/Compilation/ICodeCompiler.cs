namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// Something that can tell you whether a piece of C# compiles.
/// </summary>
/// <remarks>
/// A seam rather than an abstraction for its own sake: there is a second real backend, invoking
/// the Unity editor in batch mode, which this slice measured and deliberately did not build, and
/// the reason it is not built is a property of the tool rather than of the design. Should that
/// change, it arrives as another implementation and nothing that consumes a
/// <see cref="CompileResult"/> notices.
/// </remarks>
public interface ICodeCompiler
{
    /// <summary>What this compiler is, named in the feed so a result can be judged.</summary>
    string Name { get; }

    /// <summary>
    /// Describes what is available to compile against right now, without compiling anything.
    /// </summary>
    /// <param name="projectPath">The open Unity project, or null when none is open.</param>
    CompileReferenceSet DescribeReferences(string? projectPath);

    /// <summary>
    /// Compiles one file's worth of source.
    /// </summary>
    /// <param name="source">The code to check.</param>
    /// <param name="fileName">The name diagnostics are reported against.</param>
    /// <param name="projectPath">The open Unity project, or null when none is open.</param>
    /// <param name="ct">Cancels the compile.</param>
    /// <exception cref="CompilerUnavailableException">There is nothing to compile against.</exception>
    Task<CompileResult> CompileAsync(string source, string fileName, string? projectPath, CancellationToken ct);
}
