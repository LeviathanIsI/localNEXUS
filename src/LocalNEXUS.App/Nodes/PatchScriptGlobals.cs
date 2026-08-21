namespace LocalNEXUS.App.Nodes;

/// <summary>
/// The variables visible to a transform node's C# expression.
/// </summary>
/// <remarks>
/// The field is deliberately lower case: it is spelled exactly as the user types it in the
/// expression editor, and <c>input</c> reads better there than <c>Input</c> would.
/// </remarks>
public sealed class PatchScriptGlobals
{
    /// <summary>The value arriving on the transform node's input pin.</summary>
    public string input { get; set; } = string.Empty;
}
