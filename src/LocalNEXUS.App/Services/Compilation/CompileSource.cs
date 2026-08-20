namespace LocalNEXUS.App.Services.Compilation;

/// <summary>One file handed to a compile, and the name its diagnostics are reported under.</summary>
/// <param name="FileName">What to call it in a diagnostic.</param>
/// <param name="Source">The code itself.</param>
public sealed record CompileSource(string FileName, string Source);
