using System.Text.RegularExpressions;
using LocalNEXUS.App.Services.History;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Services.Planning;

/// <summary>
/// Decides whether a request says enough to plan from, and asks when it does not.
/// </summary>
/// <remarks>
/// The decision sits here rather than in the planner prompt because the prompt cannot make it. Two
/// attempts were made at wording a model into asking, and both failed for the same reason: an
/// instruction tuned coder model has no concept of declining to produce output, so "can this be
/// planned?" is not a question it answers no to. The second attempt moved the behaviour without
/// crossing the threshold, which is the clearest evidence there is that no wording gets there.
///
/// Asked to make it faster, against a project of five types, it planned an edit to every one of
/// them and invented a method and two fields on a class nobody had mentioned, ten times out of ten.
///
/// So this is a floor beneath the prompt rather than a replacement for it. The prompt still invites
/// a model to ask about a genuine fork, and a better model may take it; this catches the case where
/// there is nothing to plan from at all, and it cannot be talked out of because it never asks.
/// </remarks>
public static class RequestScope
{
    /// <summary>How many things to offer when asking which one was meant.</summary>
    /// <remarks>
    /// Enough to cover a small project outright and few enough to read in one line. Beyond this the
    /// ranking decides, and when the ranking has nothing to say the project's own order does, which
    /// is at least stable between runs.
    /// </remarks>
    public const int MaximumOptions = 6;

    /// <summary>
    /// Something that could be the name of a type, written where a name is written.
    /// </summary>
    /// <remarks>
    /// Three shapes, and none of them is a word a sentence merely begins with. A backticked span
    /// and a file with an extension are somebody being explicit. A capitalised word that is not the
    /// first word of its sentence is the shape of "add a Cooldown class", and the sentence position
    /// is what separates it from "Make it faster": both are one capitalised word, and only one of
    /// them is introducing a name.
    ///
    /// This is looser than what v1.30 accepts in a debate position, deliberately. There, a wrong
    /// identifier produces a confident wrong number. Here, a wrong identifier only means a question
    /// is not asked, which is the behaviour that already exists, so the error is the cheap way
    /// round.
    /// </remarks>
    private static readonly Regex Introduced = new(
        @"`[^`\r\n]{1,80}`"
        + @"|\b[A-Za-z0-9_]+\.(?:cs|json|asset|prefab|shader|md)\b"
        + @"|(?<!^)(?<![.!?;:]\s{0,4})\b[A-Z][A-Za-z0-9_]{2,}\b",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(2));

    /// <summary>
    /// True when the request gives something to plan from.
    /// </summary>
    /// <remarks>
    /// Two ways to qualify, and a request needs only one. It names something the project already
    /// has, which the index answers exactly; or it introduces a name of its own, which is what
    /// every request to create something does and what makes such a request perfectly clear
    /// despite naming nothing that exists yet.
    /// </remarks>
    public static bool IsPlannable(string request, ProjectIndexService index)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (string.IsNullOrWhiteSpace(request))
        {
            return false;
        }

        return NamesSomethingExisting(request, index) || IntroducesAName(request);
    }

    /// <summary>Whether the request names a type, member or file the project already holds.</summary>
    public static bool NamesSomethingExisting(string request, ProjectIndexService index)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (string.IsNullOrWhiteSpace(request))
        {
            return false;
        }

        var known = Vocabulary(index);

        if (known.Count == 0)
        {
            return false;
        }

        foreach (Match match in Words.Matches(request))
        {
            if (known.Contains(match.Value))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether the request puts forward a name of its own.</summary>
    public static bool IntroducesAName(string request)
        => !string.IsNullOrWhiteSpace(request) && Introduced.IsMatch(request);

    /// <summary>
    /// The one question to ask about a request that names nothing.
    /// </summary>
    /// <remarks>
    /// Built here rather than asked of the model, because the app is the thing that knows what
    /// exists. v1.15 requires a question to carry at least two concrete alternatives and the model
    /// never supplied them; the index always can.
    ///
    /// Empty when there is nothing to offer, which is a project with fewer than two types in it. A
    /// question with one option is not a question, and there is nothing to be gained by stopping a
    /// run to ask it.
    /// </remarks>
    public static IReadOnlyList<ClarificationQuestion> AskWhichOne(
        string request,
        ProjectIndexService index,
        IReadOnlyList<RankedFile> candidates)
    {
        ArgumentNullException.ThrowIfNull(index);

        var options = Options(index, candidates);

        if (options.Count < 2)
        {
            return Array.Empty<ClarificationQuestion>();
        }

        var question = new ClarificationQuestion(
            $"\"{request.Trim()}\" does not name anything in this project. Which of these did you mean?",
            options);

        return question.IsAnswerable
            ? new[] { question }
            : Array.Empty<ClarificationQuestion>();
    }

    /// <summary>
    /// What to offer, in the order worth offering it.
    /// </summary>
    /// <remarks>
    /// The ranking first, because it is the application's own answer to what a request is probably
    /// about and it costs nothing to reuse. For a request that names nothing it usually has nothing
    /// to say, since it works from terms the request shares with the project, so the project's own
    /// order is the fallback: the types it declares, in the order the index holds them, which is
    /// stable between runs on the same project.
    ///
    /// Types before files, because a person thinks in types. A file with no type in it is offered
    /// by name rather than left out, since it is still something that could be meant.
    /// </remarks>
    private static IReadOnlyList<string> Options(ProjectIndexService index, IReadOnlyList<RankedFile> candidates)
    {
        var options = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Offer(IndexedFile file)
        {
            foreach (var type in file.Types)
            {
                if (options.Count < MaximumOptions && seen.Add(type.Name))
                {
                    options.Add(type.Name);
                }
            }

            if (file.Types.Count == 0 && options.Count < MaximumOptions && seen.Add(file.FileName))
            {
                options.Add(file.FileName);
            }
        }

        foreach (var candidate in candidates ?? Array.Empty<RankedFile>())
        {
            Offer(candidate.File);
        }

        foreach (var file in index.Files)
        {
            if (options.Count >= MaximumOptions)
            {
                break;
            }

            Offer(file);
        }

        return options;
    }

    /// <summary>Every name the project answers to, for an exact match against the request.</summary>
    /// <remarks>
    /// Case sensitive, as v1.30's is, so that the health of a character in a sentence is not the
    /// type Health. A request that means the type almost always writes the type.
    /// </remarks>
    private static HashSet<string> Vocabulary(ProjectIndexService index)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in index.Files)
        {
            known.Add(file.FileName);
            known.Add(file.RelativePath);

            foreach (var type in file.Types)
            {
                known.Add(type.Name);
                known.Add(type.FullName);

                foreach (var member in type.Members)
                {
                    known.Add(member.Name);
                }
            }
        }

        return known;
    }

    /// <summary>Every token in a request that could be a name.</summary>
    private static readonly Regex Words = new(
        @"[A-Za-z_][A-Za-z0-9_]*(?:[./\\][A-Za-z0-9_]+)*",
        RegexOptions.None,
        TimeSpan.FromSeconds(2));
}
