namespace LocalNEXUS.App.Nodes;

/// <summary>How a transform node rewrites the value passing through it.</summary>
public enum TransformMode
{
    /// <summary>Substitute the input into a template, then apply find and replace pairs.</summary>
    Template,

    /// <summary>Evaluate a C# expression with the input available as <c>input</c>.</summary>
    Script
}
