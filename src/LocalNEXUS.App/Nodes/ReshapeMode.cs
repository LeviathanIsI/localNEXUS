namespace LocalNEXUS.App.Nodes;

/// <summary>How a reshape node rewrites the text passing through it.</summary>
/// <remarks>
/// Five, all mechanical and none of them inference. The node's value is that it costs nothing and
/// does the same thing every time, which is what makes it safe to leave in the middle of a graph.
///
/// The order is the order they appear in the panel, and it is the order they get used in. Inject
/// and Extract are first because they are what people actually want: standing instructions, and
/// keeping the part of a reply that was asked for. The other three are the general case and the
/// escape hatch.
/// </remarks>
public enum ReshapeMode
{
    /// <summary>
    /// Put standing text before or after whatever passes through.
    /// </summary>
    /// <remarks>
    /// The most common thing anybody wants. A house rule on the way into the coder, without
    /// editing its system prompt, and without editing five of them when the rule changes.
    /// </remarks>
    Inject,

    /// <summary>
    /// Keep the part that matches and drop the rest.
    /// </summary>
    /// <remarks>
    /// Model output is always more than was asked for. Triage emits its reasoning and then a
    /// numbered plan; this is how the plan arrives on its own.
    /// </remarks>
    Extract,

    /// <summary>Find and replace, by pattern. The general case.</summary>
    Replace,

    /// <summary>Cut to a length, so what leaves fits a context budget.</summary>
    Trim,

    /// <summary>
    /// A C# expression, for anything the four presets do not cover.
    /// </summary>
    /// <remarks>
    /// The only mode that can be unavailable. The script compiler needs the runtime assemblies as
    /// files and a single file executable keeps them inside itself, so a published build has the
    /// other four and not this one. That used to matter a great deal, because the default rule was
    /// a script; it does not now, because fence stripping lives in the model node as a regular
    /// expression.
    /// </remarks>
    Script
}

/// <summary>Which end of the text a trim cuts from.</summary>
public enum TrimFrom
{
    /// <summary>Keep the beginning, cut what runs past the limit.</summary>
    End,

    /// <summary>Keep the end, cut what comes before it.</summary>
    Start
}
