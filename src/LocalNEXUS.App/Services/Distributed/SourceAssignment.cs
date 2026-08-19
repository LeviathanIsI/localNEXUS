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

    /// <summary>Coverage depth of this section, for the chain's colour and strength bars.</summary>
    public SectionCoverage Coverage => !IsCovered
        ? SectionCoverage.Uncovered
        : Redundancy >= 2 ? SectionCoverage.Healthy : SectionCoverage.Thin;

    /// <summary>Label for the coverage chain in the panel.</summary>
    public string SourceText => Source?.DisplayName ?? "no source";

    /// <summary>The state word under the segment's strength bars.</summary>
    public string CoverageText => Coverage switch
    {
        SectionCoverage.Healthy => $"{Redundancy} candidates",
        SectionCoverage.Thin => "single source",
        _ => "no source"
    };

    /// <summary>First strength bar: anything at all stands behind the section.</summary>
    public bool Depth1 => IsCovered && Redundancy >= 1;

    /// <summary>Second strength bar: the section survives losing one source.</summary>
    public bool Depth2 => IsCovered && Redundancy >= 2;

    /// <summary>Third strength bar: comfortably covered.</summary>
    public bool Depth3 => IsCovered && Redundancy >= 3;
}
