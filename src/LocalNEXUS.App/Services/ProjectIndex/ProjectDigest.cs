using System.Text;

namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>
/// Renders the index as something a model can read inside a small context window.
/// </summary>
/// <remarks>
/// Progressive disclosure, in three widths. The map is one line per type and nothing else, and
/// covers the whole project. A candidate summary adds member signatures for the files ranking
/// picked out. Only the files that survive both are ever read from disk in full.
///
/// Bodies never appear at any width. What a request needs to know about existing code is what it
/// can call and what it can set.
/// </remarks>
public static class ProjectDigest
{
    /// <summary>
    /// The whole project as a list of what it declares, ranked candidates first so that the part
    /// most likely to matter survives the budget.
    /// </summary>
    public static string BuildMap(ProjectIndex index, IReadOnlyList<RankedFile> candidates, ContextBudget budget)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(budget);

        var builder = new StringBuilder();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ranked in candidates)
        {
            AppendFileLine(builder, ranked.File);
            written.Add(ranked.File.RelativePath);
        }

        foreach (var file in index.Files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (file.Types.Count > 0 && written.Add(file.RelativePath))
            {
                AppendFileLine(builder, file);
            }
        }

        return ContextBudget.Fit(builder.ToString().TrimEnd(), budget.MapCharacters, "the project map");
    }

    /// <summary>
    /// The candidate files with their member signatures, which is what the decision pass needs to
    /// tell a file that already does the job from one that merely sounds like it.
    /// </summary>
    public static string BuildCandidateSummary(IReadOnlyList<RankedFile> candidates, ContextBudget budget)
    {
        ArgumentNullException.ThrowIfNull(budget);

        var builder = new StringBuilder();

        foreach (var ranked in candidates)
        {
            builder.AppendLine($"{ranked.File.RelativePath}  ({ranked.Reason})");

            foreach (var type in ranked.File.Types)
            {
                builder.AppendLine($"  {DescribeKind(type.Kind)} {type.FullName}{BaseSuffix(type)}");

                foreach (var member in type.Members.Take(24))
                {
                    var note = member.IsSerialized && member.Kind == IndexedMemberKind.Field ? "   [serialized]" : string.Empty;
                    builder.AppendLine($"    {member.Signature}{note}");
                }

                if (type.Members.Count > 24)
                {
                    builder.AppendLine($"    ... {type.Members.Count - 24} more member(s)");
                }
            }

            builder.AppendLine();
        }

        return ContextBudget.Fit(builder.ToString().TrimEnd(), budget.CandidateCharacters, "candidate summaries");
    }

    /// <summary>
    /// One type as a signature block, used to show a later generation step what an earlier one in
    /// the same run already defined.
    /// </summary>
    public static string DescribeType(IndexedType type)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{DescribeKind(type.Kind)} {type.FullName}{BaseSuffix(type)}");

        foreach (var member in type.Members)
        {
            builder.AppendLine($"  {member.Signature}");
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendFileLine(StringBuilder builder, IndexedFile file)
    {
        if (file.Types.Count == 0)
        {
            return;
        }

        var types = string.Join(", ", file.Types.Select(t => $"{DescribeKind(t.Kind)} {t.Name}"));
        builder.AppendLine($"{file.RelativePath}: {types}");
    }

    private static string BaseSuffix(IndexedType type)
        => type.BaseTypes.Count == 0 ? string.Empty : " : " + string.Join(", ", type.BaseTypes);

    private static string DescribeKind(IndexedTypeKind kind) => kind switch
    {
        IndexedTypeKind.MonoBehaviour => "MonoBehaviour",
        IndexedTypeKind.ScriptableObject => "ScriptableObject",
        IndexedTypeKind.Interface => "interface",
        IndexedTypeKind.Enum => "enum",
        IndexedTypeKind.Struct => "struct",
        IndexedTypeKind.Record => "record",
        _ => "class"
    };
}
