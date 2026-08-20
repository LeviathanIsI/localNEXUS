using System.Text.RegularExpressions;

namespace LocalNEXUS.App.Services.Editing;

/// <summary>
/// Turns whatever a coder replied with into the full contents of a file.
/// </summary>
/// <remarks>
/// The caller says which format it asked for, but the reply decides what it actually is. A model
/// asked for a diff that returns a whole file has done something useful and is taken at its word;
/// a model asked for a whole file that returns a diff has not, and is also taken at its word. The
/// alternative, failing because the reply was the wrong shape, throws away a correct answer over
/// a formatting preference.
/// </remarks>
public static class CodeEditApplier
{
    /// <summary>
    /// Files below this size are rewritten whole rather than diffed under
    /// <see cref="EditFormat.Automatic"/>. A short file costs little to resend and a diff against
    /// one has almost no context to anchor to.
    /// </summary>
    public const int WholeFileThreshold = 3000;

    private static readonly Regex FencedBlock = new(
        @"(?s)^\s*```[A-Za-z0-9#+_-]*\s*\r?\n(.*?)\r?\n?```\s*$",
        RegexOptions.Compiled);

    /// <summary>Which format a task should be asked for, given the setting and the file.</summary>
    public static bool WantsWholeFile(EditFormat format, bool isNewFile, int existingLength) => format switch
    {
        EditFormat.WholeFile => true,
        EditFormat.LineTaggedDiff => false,
        _ => isNewFile || existingLength < WholeFileThreshold
    };

    /// <summary>
    /// Applies a reply to the file it was written against.
    /// </summary>
    /// <param name="reply">What the coder returned.</param>
    /// <param name="existingContent">The current file, or null when it is being created.</param>
    /// <exception cref="EditApplyException">The reply was empty, or a change block did not match.</exception>
    public static string Apply(string? reply, string? existingContent)
    {
        var body = Unfence(reply ?? string.Empty);

        if (body.Trim().Length == 0)
        {
            throw new EditApplyException("The coder returned nothing, so there is no change to apply.");
        }

        if (!LineTaggedDiff.LooksLikeDiff(body))
        {
            return Normalise(body);
        }

        if (string.IsNullOrEmpty(existingContent))
        {
            // A diff against a file that does not exist yet can only mean its added lines.
            var added = LineTaggedDiff.Parse(body).SelectMany(h => h.After).ToList();

            if (added.Count == 0)
            {
                throw new EditApplyException("The coder returned a diff for a new file, and it added no lines.");
            }

            return Normalise(string.Join(Environment.NewLine, added));
        }

        return Normalise(LineTaggedDiff.Apply(existingContent, LineTaggedDiff.Parse(body)));
    }

    /// <summary>
    /// Strips a surrounding markdown fence. Models add one despite being asked not to often
    /// enough that treating it as an error would be a choice to fail on purpose.
    /// </summary>
    public static string Unfence(string reply)
    {
        var match = FencedBlock.Match(reply);
        return match.Success ? match.Groups[1].Value : reply;
    }

    /// <summary>
    /// A file ends with exactly one newline. Unity does not care, but a project where half the
    /// generated files disagree produces diffs full of noise.
    /// </summary>
    private static string Normalise(string content)
        => content.TrimEnd('\r', '\n', ' ', '\t') + Environment.NewLine;
}
