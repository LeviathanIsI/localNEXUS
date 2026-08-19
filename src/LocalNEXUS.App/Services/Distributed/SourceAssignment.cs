namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Which source currently fills which section, and how much of the model that section is.
/// </summary>
/// <param name="Section">The slot being filled.</param>
/// <param name="Source">The source filling it, or null when nothing can. A null source in any assignment makes the plan incomplete.</param>
/// <param name="Proportion">Fraction of the model assigned to this source, in the range zero to one.</param>
/// <param name="Redundancy">How many known sources could cover this section right now, including the assigned one.</param>
public sealed record SourceAssignment(
    ModelSection Section,
    InferenceSource? Source,
    double Proportion,
    int Redundancy)
{
    /// <summary>True when a source is assigned and was available when the plan was computed.</summary>
    public bool IsCovered => Source is not null;

    /// <summary>Label for the coverage chain in the panel.</summary>
    public string SourceText => Source?.DisplayName ?? "uncovered";
}
