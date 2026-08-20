using System.Text.RegularExpressions;

namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>
/// Decides which files in the project a request is likely to be about.
/// </summary>
/// <remarks>
/// Two stages, both cheap. First a keyword score over the names that actually carry meaning: the
/// file name, the type names, the base types and the member names. Then that score is spread
/// through the reference graph by personalized PageRank, so a file nothing in the request names
/// but everything relevant depends on still surfaces. That second stage is what stops the ranker
/// being pure string matching, and it is the reason the index bothers to record which type names
/// each file mentions.
///
/// Nothing here loads a file's contents. Ranking runs over the index, and only the handful of
/// files that survive are ever read, which is the whole point when the reader is a local model
/// with a small window.
/// </remarks>
public static class RelevanceRanker
{
    /// <summary>How much of a file's rank flows onward through its references.</summary>
    private const double Damping = 0.85d;

    /// <summary>Enough iterations for the ranking order to settle on graphs of this size.</summary>
    private const int Iterations = 20;

    /// <summary>Words too common in a request to say anything about which file it concerns.</summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "that", "this", "from", "into", "make", "add", "new", "use",
        "using", "create", "build", "write", "when", "then", "should", "unity", "script", "scripts",
        "class", "game", "gameobject", "component", "system", "please", "also", "have", "has",
        "need", "needs", "want", "each", "every", "all", "any", "some", "can", "will", "must"
    };

    private static readonly Regex WordPattern = new(@"[A-Za-z][A-Za-z0-9]*", RegexOptions.Compiled);

    /// <summary>
    /// Ranks every indexed file against a request and returns the best of them.
    /// </summary>
    /// <param name="index">The project index to rank over.</param>
    /// <param name="request">What the user asked for.</param>
    /// <param name="limit">How many candidates to return.</param>
    public static IReadOnlyList<RankedFile> Rank(ProjectIndex index, string request, int limit)
    {
        ArgumentNullException.ThrowIfNull(index);

        var files = index.Files.ToList();

        if (files.Count == 0 || limit <= 0)
        {
            return Array.Empty<RankedFile>();
        }

        var terms = ExtractTerms(request);

        if (terms.Count == 0)
        {
            return Array.Empty<RankedFile>();
        }

        var matched = new List<string>[files.Count];
        var seed = new double[files.Count];

        for (var i = 0; i < files.Count; i++)
        {
            seed[i] = ScoreFile(files[i], terms, out var hits);
            matched[i] = hits;
        }

        var total = seed.Sum();

        if (total <= 0)
        {
            return Array.Empty<RankedFile>();
        }

        for (var i = 0; i < seed.Length; i++)
        {
            seed[i] /= total;
        }

        var ranked = Propagate(files, seed);

        return Enumerable.Range(0, files.Count)
            .Select(i => new RankedFile(files[i], ranked[i], matched[i]))
            .Where(r => r.Score > 0)
            .OrderByDescending(r => r.Score)
            .ThenBy(r => r.File.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .ToList();
    }

    /// <summary>
    /// The words of a request worth matching on. Identifiers are split on case as well, so that
    /// a request mentioning player health finds a PlayerHealth.
    /// </summary>
    public static IReadOnlyList<string> ExtractTerms(string request)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in WordPattern.Matches(request ?? string.Empty))
        {
            foreach (var part in SplitIdentifier(match.Value))
            {
                if (part.Length >= 3 && !StopWords.Contains(part))
                {
                    terms.Add(part);
                }
            }
        }

        return terms.ToList();
    }

    /// <summary>
    /// Splits an identifier into its words, so PlayerHealth yields player, health and playerhealth.
    /// </summary>
    public static IEnumerable<string> SplitIdentifier(string identifier)
    {
        yield return identifier;

        var start = 0;

        for (var i = 1; i <= identifier.Length; i++)
        {
            if (i == identifier.Length || (char.IsUpper(identifier[i]) && !char.IsUpper(identifier[i - 1])))
            {
                if (i - start >= 3)
                {
                    yield return identifier[start..i];
                }

                start = i;
            }
        }
    }

    /// <summary>
    /// How much a file looks like what was asked for. Type names weigh most because they are what
    /// a request is usually naming, then the file name, then members.
    /// </summary>
    private static double ScoreFile(IndexedFile file, IReadOnlyList<string> terms, out List<string> matched)
    {
        matched = new List<string>();
        var score = 0d;

        foreach (var term in terms)
        {
            var hit = 0d;

            foreach (var type in file.Types)
            {
                if (Contains(type.Name, term))
                {
                    hit = Math.Max(hit, 6d);
                }

                if (type.BaseTypes.Any(b => Contains(b, term)))
                {
                    hit = Math.Max(hit, 2d);
                }

                if (type.Members.Any(m => Contains(m.Name, term)))
                {
                    hit = Math.Max(hit, 1.5d);
                }
            }

            if (Contains(file.FileName, term))
            {
                hit = Math.Max(hit, 4d);
            }

            if (Contains(file.Namespace, term))
            {
                hit = Math.Max(hit, 1d);
            }

            if (hit > 0)
            {
                score += hit;
                matched.Add(term);
            }
        }

        return score;
    }

    private static bool Contains(string haystack, string needle)
        => haystack.Length > 0 && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Personalized PageRank over the reference graph, restarting from the keyword scores.
    /// </summary>
    /// <remarks>
    /// Edges run from the file that mentions a type to the file that declares it, so rank flows
    /// towards definitions. A file with no outgoing edges hands its rank back to the seed rather
    /// than losing it, which is the usual treatment of a dangling node and stops a project full
    /// of leaf scripts from draining the ranking.
    /// </remarks>
    private static double[] Propagate(IReadOnlyList<IndexedFile> files, double[] seed)
    {
        var indexOfFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var declaringFile = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < files.Count; i++)
        {
            indexOfFile[files[i].RelativePath] = i;

            foreach (var type in files[i].Types)
            {
                declaringFile.TryAdd(type.Name, i);
            }
        }

        var edges = new List<int>[files.Count];

        for (var i = 0; i < files.Count; i++)
        {
            var targets = new HashSet<int>();

            foreach (var name in files[i].ReferencedTypeNames)
            {
                if (declaringFile.TryGetValue(name, out var target) && target != i)
                {
                    targets.Add(target);
                }
            }

            edges[i] = targets.ToList();
        }

        var rank = (double[])seed.Clone();
        var next = new double[files.Count];

        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            Array.Clear(next);

            var dangling = 0d;

            for (var i = 0; i < files.Count; i++)
            {
                if (edges[i].Count == 0)
                {
                    dangling += rank[i];
                    continue;
                }

                var share = rank[i] / edges[i].Count;

                foreach (var target in edges[i])
                {
                    next[target] += share;
                }
            }

            for (var i = 0; i < files.Count; i++)
            {
                next[i] = ((1d - Damping) * seed[i]) + (Damping * (next[i] + (dangling * seed[i])));
            }

            (rank, next) = (next, rank);
        }

        // The keyword score still dominates: propagation adds files the request did not name but
        // must not bury the ones it did.
        for (var i = 0; i < rank.Length; i++)
        {
            rank[i] += seed[i] * 2d;
        }

        return rank;
    }
}
