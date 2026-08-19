namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// How well covered a section, or a whole model, is right now. Drives the coverage chain and
/// the model list through the SectionCoverage brush keys.
/// </summary>
/// <remarks>
/// <see cref="Starting"/> comes first so that a value nobody has computed yet reads as in
/// progress rather than as either verdict, and it is deliberately not a shade of
/// <see cref="Uncovered"/>: a section that is still loading has not failed at anything.
/// </remarks>
public enum SectionCoverage
{
    /// <summary>Coming up. Nothing is known to be wrong, and nothing is ready either.</summary>
    Starting,

    /// <summary>More than one source could fill the slot. Losing one does not break the pipeline.</summary>
    Healthy,

    /// <summary>Live, but exactly one source stands behind it. Losing that source breaks the pipeline.</summary>
    Thin,

    /// <summary>No source fills the slot. There is no valid pipeline.</summary>
    Uncovered
}
