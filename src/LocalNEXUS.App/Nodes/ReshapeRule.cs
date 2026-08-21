using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Thrown when a rule cannot be understood or cannot be applied.
/// </summary>
/// <remarks>
/// A refusal rather than passing the code through untouched, and the choice is deliberate. This
/// node exists to change what goes through it, so a rule that does not work means the thing
/// downstream, a compile check or a file writer, receives something nobody asked for. Under
/// pass through, the run then reports success for a file that was never patched, and the reason
/// is invisible. Failing here names the rule and what was wrong with it, at the node that owns it.
///
/// A rule that is understood, applies cleanly and happens to match nothing is not this. That is a
/// rule doing its job on input it has nothing to say about, and the code passes through.
/// </remarks>
public sealed class ReshapeRuleException : Exception
{
    public ReshapeRuleException(string message)
        : base(message)
    {
    }

    public ReshapeRuleException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// One rule for reshaping code: which form it takes, and its parts.
/// </summary>
/// <remarks>
/// The same type whether the rule was typed on the node or arrived on the pin, which is the point.
/// A model that writes a rule and a person who types one are producing the same thing, and the
/// node applies it the same way either way. Nothing here calls a model: the rule is authored
/// somewhere else and executed here, mechanically, so a patch is fast, repeatable, and never sends
/// the code anywhere to be reformatted.
/// </remarks>
/// <param name="Kind">Which form the rule takes.</param>
/// <param name="Primary">The template, the pattern, or the expression, depending on the kind.</param>
/// <param name="Replacement">What a regex rule replaces its match with. Empty for the other kinds.</param>
public sealed record ReshapeRule(ReshapeMode Kind, string Primary, string Replacement)
{
    /// <summary>
    /// Reads a rule that arrived on the pin.
    /// </summary>
    /// <param name="text">Whatever came down the wire.</param>
    /// <param name="fallbackKind">The form the node is configured for, used when the text does not say.</param>
    /// <param name="fallbackReplacement">The node's own replacement, for a bare pattern that gives none.</param>
    /// <remarks>
    /// Four steps, in order, and the order is what makes it predictable rather than clever.
    ///
    /// A JSON object wins outright. That is the form to ask a model for, because it says what it
    /// is: a <c>kind</c> of regex, template or script, and then <c>pattern</c> and
    /// <c>replacement</c>, or <c>template</c>, or <c>expression</c>.
    ///
    /// Otherwise, text containing the input placeholder is a template, because nothing else uses
    /// that marker and a template without it would do nothing.
    ///
    /// Otherwise, the node's own mode decides the form. The node says what shape of rule it
    /// expects and the pin supplies the content, so a model wired into a node set to script writes
    /// an expression and a model wired into one set to regex writes a pattern.
    ///
    /// A bare regex is read as the pattern on the first line and the replacement on the rest. A
    /// pattern with no second line keeps the replacement the node already has, rather than
    /// silently deleting every match, which is what an empty replacement would mean.
    /// </remarks>
    /// <exception cref="ReshapeRuleException">The rule is empty, or says it is a kind nobody knows.</exception>
    public static ReshapeRule Parse(string text, ReshapeMode fallbackKind, string fallbackReplacement)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ReshapeRuleException(
                "The rule pin is wired but nothing arrived on it. Disconnect it to use the rule on the node, "
                + "or fix whatever should be producing one.");
        }

        var trimmed = text.Trim();

        if (trimmed.StartsWith('{') && TryParseJson(trimmed, fallbackKind, fallbackReplacement, out var structured))
        {
            return structured;
        }

        // The placeholder is the marker for a template, and inject is the mode that applies one.
        // The friendly editor and the form a model writes are the same thing underneath.
        if (trimmed.Contains(ReshapeNode.InputPlaceholder, StringComparison.Ordinal))
        {
            return new ReshapeRule(ReshapeMode.Inject, trimmed, string.Empty);
        }

        if (fallbackKind == ReshapeMode.Script)
        {
            return new ReshapeRule(ReshapeMode.Script, trimmed, string.Empty);
        }

        if (fallbackKind is ReshapeMode.Inject or ReshapeMode.Extract or ReshapeMode.Trim)
        {
            return new ReshapeRule(fallbackKind, trimmed, string.Empty);
        }

        var split = trimmed.Split('\n', 2);
        var pattern = split[0].TrimEnd('\r');
        var replacement = split.Length > 1 ? split[1].Trim() : fallbackReplacement;

        return new ReshapeRule(ReshapeMode.Replace, pattern, replacement);
    }

    /// <summary>
    /// The kind a rule says it is, accepting the names the modes used to have.
    /// </summary>
    /// <remarks>
    /// Regex and template were mode names before the five presets existed, and they are what a
    /// model that has read about this will write. Accepting them costs two lines and refusing them
    /// would fail a rule that is perfectly clear about what it wants.
    /// </remarks>
    private static ReshapeMode ReadKind(string? kindText, ReshapeMode fallback)
    {
        if (kindText is null)
        {
            return fallback;
        }

        if (Enum.TryParse<ReshapeMode>(kindText, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return kindText.ToLowerInvariant() switch
        {
            "template" => ReshapeMode.Inject,
            "regex" => ReshapeMode.Replace,
            "prepend" or "append" => ReshapeMode.Inject,
            _ => throw new ReshapeRuleException(
                $"The rule says its kind is \"{kindText}\", which is not one this node knows. "
                + "Use inject, extract, replace, trim or script.")
        };
    }

    private static bool TryParseJson(
        string text,
        ReshapeMode fallbackKind,
        string fallbackReplacement,
        out ReshapeRule rule)
    {
        rule = new ReshapeRule(fallbackKind, string.Empty, string.Empty);

        JsonObject? json;
        try
        {
            json = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            // Text that opens with a brace and is not JSON is not a malformed rule. A regex may
            // legitimately begin with a quantifier, so this falls through to the other forms.
            return false;
        }

        if (json is null)
        {
            return false;
        }

        var kindText = json["kind"]?.GetValue<string>();

        var kind = ReadKind(kindText, fallbackKind);

        var primary = kind switch
        {
            ReshapeMode.Inject => json["template"]?.GetValue<string>() ?? json["pattern"]?.GetValue<string>(),
            ReshapeMode.Script => json["expression"]?.GetValue<string>(),
            ReshapeMode.Trim => json["length"]?.GetValue<int>().ToString(),
            _ => json["pattern"]?.GetValue<string>()
        };

        if (string.IsNullOrEmpty(primary))
        {
            throw new ReshapeRuleException(
                $"The rule says it is a {kind.ToString().ToLowerInvariant()} rule but carries nothing to apply. "
                + "Inject needs a template, extract and replace need a pattern, trim needs a length, "
                + "and script needs an expression.");
        }

        rule = new ReshapeRule(
            kind,
            primary,
            json["replacement"]?.GetValue<string>() ?? fallbackReplacement);

        return true;
    }
}
