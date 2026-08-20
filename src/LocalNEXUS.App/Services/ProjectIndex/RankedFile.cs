namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>One candidate file and why the ranker thought it was relevant.</summary>
/// <param name="File">The file itself.</param>
/// <param name="Score">Its final rank, after the reference graph has had its say.</param>
/// <param name="Keywords">The request words that matched, which is what makes a rank explainable.</param>
public sealed record RankedFile(IndexedFile File, double Score, IReadOnlyList<string> Keywords)
{
    /// <summary>Why this file was offered, in a few words, for the activity feed.</summary>
    public string Reason => Keywords.Count == 0
        ? "reached through the reference graph"
        : $"matches {string.Join(", ", Keywords)}";
}
