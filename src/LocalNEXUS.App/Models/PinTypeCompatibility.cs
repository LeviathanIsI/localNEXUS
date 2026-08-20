namespace LocalNEXUS.App.Models;

/// <summary>
/// Decides which pin types may flow into which.
/// </summary>
/// <remarks>
/// The rule is equality with one deliberate exception: a <see cref="PinType.Code"/> output may
/// feed a <see cref="PinType.Text"/> input.
/// <para>
/// Without that exception a model node, which takes Text and emits Code, could only ever be fed
/// by an input node, so chaining a planning model into a coding model would be impossible and
/// the core workflow of the application could not be built. The exception is one directional on
/// purpose: code is text, but arbitrary text is not code, so a Text output still cannot reach a
/// Code input such as the one on the node that writes files.
/// </para>
/// <para>
/// New pin types are added to <see cref="PinType"/> and, if they need to interoperate with an
/// existing type, given a rule here. Nothing else consults the type system.
/// </para>
/// </remarks>
public static class PinTypeCompatibility
{
    /// <summary>True when a value produced as <paramref name="source"/> may be consumed as <paramref name="target"/>.</summary>
    public static bool CanFlow(PinType source, PinType target)
        => source == target || (source is PinType.Code && target is PinType.Text);

    /// <summary>A short explanation of why a flow is not permitted, for the pending wire label.</summary>
    public static string DescribeRefusal(PinType source, PinType target)
        => $"{source} does not fit {target}";

    /// <summary>A short description of a permitted flow, for the pending wire label.</summary>
    public static string DescribeFlow(PinType source, PinType target)
        => source == target ? source.ToString() : $"{source} to {target}";
}
