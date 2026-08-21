using System.Text;
using System.Text.RegularExpressions;

namespace LocalNEXUS.App.Services.Debate;

/// <summary>One identifier both sides named, with opposite intentions for it.</summary>
/// <param name="Identifier">What they both named.</param>
/// <param name="First">What the first side wants to do to it.</param>
/// <param name="Second">What the second side wants to do to it.</param>
public sealed record Contradiction(string Identifier, string First, string Second)
{
    /// <summary>The disagreement in one line.</summary>
    public override string ToString() => $"{Identifier}: {First} against {Second}";
}

/// <summary>
/// How far apart two positions are, and why.
/// </summary>
/// <param name="Score">Agreement from 0 to 100, or null when there was nothing to measure.</param>
/// <param name="SharedIdentifiers">Types, members and files both sides named.</param>
/// <param name="FirstOnlyIdentifiers">Named by the first side only.</param>
/// <param name="SecondOnlyIdentifiers">Named by the second side only.</param>
/// <param name="SharedIntents">Things both sides propose doing.</param>
/// <param name="FirstOnlyIntents">Proposed by the first side only.</param>
/// <param name="SecondOnlyIntents">Proposed by the second side only.</param>
/// <param name="Contradictions">Where they named the same thing and want opposite things done to it.</param>
public sealed record Convergence(
    int? Score,
    IReadOnlyList<string> SharedIdentifiers,
    IReadOnlyList<string> FirstOnlyIdentifiers,
    IReadOnlyList<string> SecondOnlyIdentifiers,
    IReadOnlyList<string> SharedIntents,
    IReadOnlyList<string> FirstOnlyIntents,
    IReadOnlyList<string> SecondOnlyIntents,
    IReadOnlyList<Contradiction> Contradictions)
{
    /// <summary>Nothing concrete was said by either side, so there is nothing to compare.</summary>
    public static Convergence Unmeasurable { get; } = new(
        null,
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        Array.Empty<Contradiction>());

    /// <summary>The score as a phrase, or that it could not be taken.</summary>
    public string Text => Score is { } value ? $"{value} percent" : "not measurable";

    /// <summary>
    /// The whole working, so the number can be argued with.
    /// </summary>
    /// <remarks>
    /// A number nobody can interrogate is a number nobody trusts, and this one is arithmetic on a
    /// handful of words: if the weighting is wrong, this is where that becomes obvious.
    /// </remarks>
    public string Breakdown()
    {
        var builder = new StringBuilder();

        builder.AppendLine(ConvergenceMeter.WeightingSummary);
        builder.AppendLine();

        Section(builder, "Named by both", SharedIdentifiers);
        Section(builder, "Named only by the first", FirstOnlyIdentifiers);
        Section(builder, "Named only by the second", SecondOnlyIdentifiers);
        Section(builder, "Both propose", SharedIntents);
        Section(builder, "Only the first proposes", FirstOnlyIntents);
        Section(builder, "Only the second proposes", SecondOnlyIntents);

        if (Contradictions.Count > 0)
        {
            builder.AppendLine($"Directly at odds ({Contradictions.Count}):");

            foreach (var contradiction in Contradictions)
            {
                builder.AppendLine($"  {contradiction}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static void Section(StringBuilder builder, string label, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        builder.AppendLine($"{label} ({items.Count}): {string.Join(", ", items.Take(20))}"
                           + (items.Count > 20 ? ", ..." : string.Empty));
    }
}

/// <summary>
/// Measures how far two positions agree, by reading them rather than by asking a model.
/// </summary>
/// <remarks>
/// This used to be a model call, and because a debate has exactly two model pins that call went to
/// one of the debaters. A participant grading the argument it had just taken part in is a softer
/// number than it looks, and it cost an extra call every round. It is arithmetic now: free, the
/// same every time, and it can be shown its working.
///
/// It is deliberately not word overlap. Overlap measures whether two positions were written alike,
/// not whether they propose the same thing, so two models describing an identical approach in
/// different prose score badly and two models disagreeing in the same house style score well. What
/// a debate about code is actually about is narrower than prose and can be read directly.
///
/// Three things are counted and the weighting is stated in <see cref="WeightingSummary"/>.
///
/// Identifiers carry most of it. Two positions both naming InventorySlot and PickupHandler agree
/// about the approach whatever words are wrapped around them, and two naming entirely different
/// types do not, however similarly they read.
///
/// Verbs of intent carry the rest, because they are what each side is proposing to do. Create and
/// edit and replace are the argument; the nouns are only what it is about.
///
/// Everything else is worth nothing and is given nothing. Connectives, hedging and restatements of
/// the question are where naive overlap goes wrong, and a small weight on them would be the same as
/// none for ranking while being harder to explain.
///
/// Contradiction is subtracted, because it is the case overlap misses completely: one side saying
/// extend and the other saying replace, about the same type, looks like agreement to anything
/// counting shared words.
/// </remarks>
public static class ConvergenceMeter
{
    /// <summary>How much of the score the identifiers are worth.</summary>
    public const double IdentifierWeight = 0.70d;

    /// <summary>How much of the score the verbs of intent are worth.</summary>
    public const double IntentWeight = 0.30d;

    /// <summary>What one identifier with opposite intentions costs.</summary>
    public const int ContradictionPenalty = 20;

    /// <summary>The most contradiction can take off, so a single argument cannot zero the score.</summary>
    public const int MaximumPenalty = 60;

    /// <summary>What a sentence ends with, for deciding which verbs are about which name.</summary>
    private static readonly char[] SentenceEnds = { '.', '!', '?', ';', '\n', '\r' };

    /// <summary>The weighting, in one line, shown above every breakdown.</summary>
    public static string WeightingSummary =>
        $"Identifiers are {IdentifierWeight * 100:0} percent of the score and verbs of intent "
        + $"{IntentWeight * 100:0} percent, both measured as the share of everything named that both "
        + $"sides named. Everything else counts for nothing. Each thing they both named but want "
        + $"opposite things done to takes off {ContradictionPenalty}, up to {MaximumPenalty}.";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Types, members and files. Backticked spans, anything that looks like a path, and names with
    /// more than one hump in them.
    /// </summary>
    /// <remarks>
    /// More than one hump is what keeps ordinary sentences out. A rule that took every capitalised
    /// word would count The and Unity and Create as things being proposed, and a rule that took
    /// every capitalised word at the start of a sentence would count most of the prose. The cost is
    /// that a single word type name like Slot is only seen when a model marks it up or writes it as
    /// a path, which is the right way round: missing one is a smaller error than inventing twenty.
    /// </remarks>
    private static readonly Regex Identifiers = new(
        @"`([^`\r\n]{1,80})`"
        + @"|\b([A-Za-z0-9_]+(?:[/\\][A-Za-z0-9_.]+)+\.[A-Za-z0-9]{1,6})\b"
        + @"|\b([A-Za-z0-9_]+\.(?:cs|json|asset|prefab|shader|md))\b"
        + @"|\b([A-Z][a-z0-9]+(?:[A-Z][a-z0-9]*)+)\b"
        + @"|\b([a-z][a-z0-9]*(?:[A-Z][a-z0-9]*)+)\b",
        RegexOptions.None,
        Timeout);

    /// <summary>
    /// The verbs a proposal is made of, each with the forms a model actually writes.
    /// </summary>
    /// <remarks>
    /// A fixed list rather than anything clever. These are the moves available when changing a
    /// codebase, and a verb that is not one of them is not a proposal about the code.
    /// </remarks>
    private static readonly Dictionary<string, string[]> Intents = new(StringComparer.OrdinalIgnoreCase)
    {
        ["create"] = new[] { "create", "creates", "creating", "created", "new", "introduce", "introduces", "introducing", "add", "adds", "adding" },
        ["reuse"] = new[] { "reuse", "reuses", "reusing", "existing", "keep", "keeps", "keeping" },
        ["extend"] = new[] { "extend", "extends", "extending", "inherit", "inherits", "inheriting", "subclass", "derive", "derives" },
        ["edit"] = new[] { "edit", "edits", "editing", "modify", "modifies", "modifying", "change", "changes", "changing", "update", "updates", "updating" },
        ["replace"] = new[] { "replace", "replaces", "replacing", "rewrite", "rewrites", "rewriting", "supersede", "supersedes" },
        ["remove"] = new[] { "remove", "removes", "removing", "delete", "deletes", "deleting", "drop", "drops", "dropping" },
        ["split"] = new[] { "split", "splits", "splitting", "separate", "separates", "separating", "extract", "extracts", "extracting" },
        ["merge"] = new[] { "merge", "merges", "merging", "combine", "combines", "combining", "fold", "folds", "folding" },
        ["rename"] = new[] { "rename", "renames", "renaming" },
        ["move"] = new[] { "move", "moves", "moving", "relocate", "relocates" },
        ["expose"] = new[] { "expose", "exposes", "exposing", "serialize", "serialized", "serialise", "serialised", "public" },
        ["cache"] = new[] { "cache", "caches", "caching", "memoize", "memoise" },
        ["inject"] = new[] { "inject", "injects", "injecting", "wire", "wires", "wiring" }
    };

    /// <summary>
    /// Which intents cannot both be right about the same thing.
    /// </summary>
    /// <remarks>
    /// Only pairs that are genuinely exclusive. Creating a type and extending one are different
    /// answers to the same question; editing a type and exposing a field on it are not, and
    /// treating them as a contradiction would punish two sides for agreeing in detail.
    /// </remarks>
    private static readonly (string First, string Second)[] Opposed =
    {
        ("create", "reuse"),
        ("create", "extend"),
        ("create", "edit"),
        ("extend", "replace"),
        ("extend", "remove"),
        ("reuse", "replace"),
        ("reuse", "remove"),
        ("edit", "replace"),
        ("edit", "remove"),
        ("split", "merge"),
        ("keep", "remove")
    };

    /// <summary>Reads two positions and says how far apart they are.</summary>
    public static Convergence Measure(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
        {
            return Convergence.Unmeasurable;
        }

        var firstIdentifiers = ReadIdentifiers(first);
        var secondIdentifiers = ReadIdentifiers(second);

        var firstIntents = ReadIntents(first);
        var secondIntents = ReadIntents(second);

        var identifierShare = Share(firstIdentifiers, secondIdentifiers);
        var intentShare = Share(firstIntents, secondIntents);

        if (identifierShare is null && intentShare is null)
        {
            // Neither side named anything or proposed anything. That is two pieces of prose about
            // nothing in particular, and reporting a number for it would be inventing one.
            return Convergence.Unmeasurable;
        }

        var contradictions = FindContradictions(first, second, firstIdentifiers, secondIdentifiers);

        // Whichever halves exist are renormalised over themselves, so a debate that named types and
        // proposed nothing is scored on what it did say rather than being halved for silence.
        var weight = (identifierShare is null ? 0d : IdentifierWeight) + (intentShare is null ? 0d : IntentWeight);

        var raw = ((identifierShare ?? 0d) * IdentifierWeight + (intentShare ?? 0d) * IntentWeight) / weight;

        var penalty = Math.Min(MaximumPenalty, contradictions.Count * ContradictionPenalty);
        var score = Math.Clamp((int)Math.Round(raw * 100d) - penalty, 0, 100);

        return new Convergence(
            score,
            Sorted(firstIdentifiers.Intersect(secondIdentifiers, StringComparer.OrdinalIgnoreCase)),
            Sorted(firstIdentifiers.Except(secondIdentifiers, StringComparer.OrdinalIgnoreCase)),
            Sorted(secondIdentifiers.Except(firstIdentifiers, StringComparer.OrdinalIgnoreCase)),
            Sorted(firstIntents.Intersect(secondIntents, StringComparer.OrdinalIgnoreCase)),
            Sorted(firstIntents.Except(secondIntents, StringComparer.OrdinalIgnoreCase)),
            Sorted(secondIntents.Except(firstIntents, StringComparer.OrdinalIgnoreCase)),
            contradictions);
    }

    /// <summary>How much of everything named by either side was named by both.</summary>
    private static double? Share(IReadOnlySet<string> first, IReadOnlySet<string> second)
    {
        if (first.Count == 0 && second.Count == 0)
        {
            return null;
        }

        var union = new HashSet<string>(first, StringComparer.OrdinalIgnoreCase);
        union.UnionWith(second);

        var shared = first.Count(second.Contains);

        return union.Count == 0 ? null : (double)shared / union.Count;
    }

    /// <summary>
    /// Finds the things both sides named and want opposite things done to.
    /// </summary>
    /// <remarks>
    /// A verb counts as being about an identifier when it sits within a short window of it in the
    /// same piece of text. That is crude and it is the right kind of crude: a proposal about a type
    /// is written near the type's name, and widening the window until it is always right would
    /// attach every verb in the position to every name in it.
    /// </remarks>
    private static IReadOnlyList<Contradiction> FindContradictions(
        string first,
        string second,
        IReadOnlySet<string> firstIdentifiers,
        IReadOnlySet<string> secondIdentifiers)
    {
        var shared = firstIdentifiers
            .Where(secondIdentifiers.Contains)
            .OrderBy(i => i, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (shared.Count == 0)
        {
            return Array.Empty<Contradiction>();
        }

        var found = new List<Contradiction>();

        foreach (var identifier in shared)
        {
            var firstNear = IntentsNear(first, identifier);
            var secondNear = IntentsNear(second, identifier);

            foreach (var (a, b) in Opposed)
            {
                if (firstNear.Contains(a) && secondNear.Contains(b) && !firstNear.Contains(b) && !secondNear.Contains(a))
                {
                    found.Add(new Contradiction(identifier, a, b));
                    break;
                }

                if (firstNear.Contains(b) && secondNear.Contains(a) && !firstNear.Contains(a) && !secondNear.Contains(b))
                {
                    found.Add(new Contradiction(identifier, b, a));
                    break;
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The intents written in the same sentence as one identifier, anywhere it appears.
    /// </summary>
    /// <remarks>
    /// The sentence, not a window of characters around it. A window wide enough to catch the verb
    /// that belongs to a name is also wide enough to catch the verbs belonging to the names either
    /// side of it, which reads a position proposing to extend one type and introduce another as
    /// proposing both about both, and then calls that a contradiction with anyone who did the same
    /// thing in a different order. A sentence is where a proposal about one thing is written.
    /// </remarks>
    private static HashSet<string> IntentsNear(string text, string identifier)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sentence in text.Split(SentenceEnds, StringSplitOptions.RemoveEmptyEntries))
        {
            if (sentence.Contains(identifier, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var intent in ReadIntents(sentence))
                {
                    found.Add(intent);
                }
            }
        }

        return found;
    }

    private static HashSet<string> ReadIdentifiers(string text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (Match match in Identifiers.Matches(text))
            {
                for (var group = 1; group < match.Groups.Count; group++)
                {
                    if (!match.Groups[group].Success)
                    {
                        continue;
                    }

                    var value = match.Groups[group].Value.Trim();

                    // A backticked span can be a whole phrase. Only a single token is a name.
                    if (value.Length is > 1 and < 80 && !value.Contains(' ', StringComparison.Ordinal))
                    {
                        found.Add(value);
                    }
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // A position long enough to take two seconds to read is one this cannot score, and an
            // unmeasurable round is a state that already exists.
        }

        return found;
    }

    private static HashSet<string> ReadIntents(string text)
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var words = text.Split(
            new[] { ' ', '\t', '\r', '\n', '.', ',', ';', ':', '(', ')', '`', '"', '\'', '/', '\\' },
            StringSplitOptions.RemoveEmptyEntries);

        var vocabulary = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in words)
        {
            vocabulary.Add(word);
        }

        foreach (var (intent, forms) in Intents)
        {
            if (forms.Any(vocabulary.Contains))
            {
                found.Add(intent);
            }
        }

        return found;
    }

    private static IReadOnlyList<string> Sorted(IEnumerable<string> items)
        => items.OrderBy(i => i, StringComparer.OrdinalIgnoreCase).ToList();
}
