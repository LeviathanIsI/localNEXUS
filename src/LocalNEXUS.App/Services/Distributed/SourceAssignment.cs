namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Which source currently holds which section, and how much slack stands behind it.
/// </summary>
/// <remarks>
/// Both facts come from the mesh: the placement from the reported stage topology, the slack
/// from how many usable peers are not already holding a stage of this model. Nothing here is
/// planned by this install.
/// </remarks>
/// <param name="Section">The slot being filled.</param>
/// <param name="Source">The source holding it, or null when the mesh has not placed it. A null source in any assignment makes the plan incomplete.</param>
/// <param name="IsReady">Whether the engine reports this stage as loaded and serving.</param>
/// <param name="StateText">The engine's own word for the stage state, shown when it is not ready.</param>
/// <param name="SpareSources">Usable peers not already holding a stage of this model, which is the slack the mesh could rebalance onto.</param>
public sealed record SourceAssignment(
    ModelSection Section,
    InferenceSource? Source,
    bool IsReady,
    string StateText,
    int SpareSources)
{
    /// <summary>True when a source holds this section and the engine reports it serving.</summary>
    public bool IsCovered => Source is not null && IsReady;

    /// <summary>Coverage depth of this section, for the chain's colour and strength bars.</summary>
    public SectionCoverage Coverage => !IsCovered
        ? SectionCoverage.Uncovered
        : SpareSources >= 1 ? SectionCoverage.Healthy : SectionCoverage.Thin;

    /// <summary>Label for the coverage chain in the panel.</summary>
    public string SourceText => Source?.DisplayName ?? "no source";

    /// <summary>The state word under the segment's strength bars.</summary>
    public string CoverageText => Coverage switch
    {
        SectionCoverage.Healthy => SpareSources == 1 ? "1 spare source" : $"{SpareSources} spare sources",
        SectionCoverage.Thin => "no spare source",
        _ => Source is null ? "not placed" : StateText
    };

    /// <summary>First strength bar: the section is held and serving.</summary>
    public bool Depth1 => IsCovered;

    /// <summary>Second strength bar: a spare source exists for the mesh to move this stage to.</summary>
    public bool Depth2 => IsCovered && SpareSources >= 1;

    /// <summary>Third strength bar: more than one spare source.</summary>
    public bool Depth3 => IsCovered && SpareSources >= 2;
}
