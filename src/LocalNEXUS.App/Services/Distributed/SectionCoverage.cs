namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// How well covered a section, or a whole model, is right now. Drives the coverage chain and
/// the model list through the SectionCoverage brush keys.
/// </summary>
public enum SectionCoverage
{
    /// <summary>More than one source could fill the slot. Losing one does not break the pipeline.</summary>
    Healthy,

    /// <summary>Live, but exactly one source stands behind it. Losing that source breaks the pipeline.</summary>
    Thin,

    /// <summary>No source fills the slot. There is no valid pipeline.</summary>
    Uncovered
}
