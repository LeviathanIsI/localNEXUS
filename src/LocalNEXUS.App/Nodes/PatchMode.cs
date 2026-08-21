namespace LocalNEXUS.App.Nodes;

/// <summary>How a transform node rewrites the value passing through it.</summary>
public enum PatchMode
{
    /// <summary>
    /// Match a regular expression against the input and replace what it finds.
    /// </summary>
    /// <remarks>
    /// The default, and the one that has to work everywhere. Stripping a markdown fence off a
    /// model reply is what the repair loop depends on, and expressing that as a script meant it
    /// stopped working in every single file build, because the script compiler needs the runtime
    /// assemblies as files and a single file executable keeps them inside itself. A regular
    /// expression needs nothing but the regular expression engine, which is always there.
    /// </remarks>
    Regex,

    /// <summary>Substitute the input into a template, then apply find and replace pairs.</summary>
    Template,

    /// <summary>Evaluate a C# expression with the input available as <c>input</c>.</summary>
    Script
}
