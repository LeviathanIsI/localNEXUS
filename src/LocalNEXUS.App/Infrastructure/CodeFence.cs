using System.Text.RegularExpressions;

namespace LocalNEXUS.App.Infrastructure;

/// <summary>
/// Takes a markdown code fence off a reply that is nothing but a code fence.
/// </summary>
/// <remarks>
/// Models wrap code in a fence whatever the prompt says, and a fenced reply is not a valid C#
/// file. Undoing that used to be a node somebody had to wire into every graph, which is boilerplate
/// for an artifact of how models format text: nobody asked for the fence, so nothing should need
/// wiring to remove it.
///
/// A regular expression and nothing else. The rule this replaces was a Roslyn script expression,
/// and the script compiler cannot be built inside a single file executable, so every published
/// build shipped a fence stripper that quietly did nothing. The regular expression engine is part
/// of the runtime and is there in every build, which is the whole reason this is written this way.
///
/// It only unwraps a reply that is entirely one fence. A reply with prose around a fence, or with
/// two of them, is left alone: that is documentation or an explanation, and cutting it down to the
/// first code block would throw away what was actually asked for.
/// </remarks>
public static class CodeFence
{
    /// <summary>
    /// A whole reply that is one fenced block, with the contents captured.
    /// </summary>
    /// <remarks>
    /// Anchored at both ends, so anything outside the fence stops it matching. The opening line
    /// may carry a language tag and trailing spaces, and the closing fence may or may not have a
    /// newline before it, which are the two shapes models actually emit.
    /// </remarks>
    private const string Pattern = @"(?s)\A\s*```[A-Za-z0-9#+_-]*[ \t]*\r?\n(.*?)\r?\n?```\s*\z";

    /// <summary>How long the match may run before it is abandoned.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Interpreted, not compiled, and that is on purpose.
    /// </summary>
    /// <remarks>
    /// Compiling a pattern emits IL at run time, which is the one part of the regular expression
    /// engine that behaves differently in a trimmed or ahead of time build. This runs once per
    /// model reply against a few kilobytes, so compiling buys nothing measurable and would put the
    /// only environment sensitive dependency in the whole mechanism back into it. What replaced
    /// the Roslyn script has no dependency on files, on reflection, or on code generation.
    /// </remarks>
    private static readonly Regex Fence = new(Pattern, RegexOptions.None, Timeout);

    /// <summary>The reply with its fence removed, or the reply unchanged when it has none.</summary>
    public static string Strip(string reply)
    {
        if (string.IsNullOrEmpty(reply) || !reply.Contains("```", StringComparison.Ordinal))
        {
            return reply;
        }

        try
        {
            var match = Fence.Match(reply);
            return match.Success ? match.Groups[1].Value : reply;
        }
        catch (RegexMatchTimeoutException)
        {
            // A reply pathological enough to take two seconds to look at is a reply to leave
            // alone. Failing a run over the formatting of something that arrived correctly would
            // be a poor trade.
            return reply;
        }
    }
}
