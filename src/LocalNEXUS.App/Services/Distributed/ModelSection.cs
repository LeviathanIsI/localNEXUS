namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// One slot in a split model: a contiguous range of layers that some source has to hold for
/// the pipeline to exist. Sections are the core concept of distributed inference here; peers
/// and machines are just whatever happens to fill them.
/// </summary>
/// <remarks>
/// A section is a Skippy stage. The engine plans the ranges and reports them; this record is
/// the shape the chain draws them in. Engine ranges are half open, so the conversion to an
/// inclusive last layer happens once, where the report is read, and never again.
/// </remarks>
/// <param name="Index">Position of this section in the pipeline, starting at zero.</param>
/// <param name="ModelId">The model this section belongs to, as the mesh identifies it.</param>
/// <param name="FirstLayer">First transformer layer this section covers, inclusive.</param>
/// <param name="LastLayer">Last transformer layer this section covers, inclusive. Below <see cref="FirstLayer"/> when the mesh has not reported the model's shape yet.</param>
public sealed record ModelSection(
    int Index,
    string ModelId,
    int FirstLayer,
    int LastLayer)
{
    /// <summary>
    /// True once the mesh has reported how many layers the model has. Before that a section is
    /// still a real slot in the pipeline, but its bounds are unknown and saying "layers 0-0"
    /// would be inventing one.
    /// </summary>
    public bool HasKnownRange => LastLayer >= FirstLayer;

    /// <summary>Number of layers in this section, or zero while the range is unknown.</summary>
    public int LayerCount => HasKnownRange ? LastLayer - FirstLayer + 1 : 0;

    /// <summary>Short label for the panel and for refusal messages.</summary>
    public string Label => HasKnownRange
        ? $"section {Index + 1} (layers {FirstLayer}-{LastLayer})"
        : $"section {Index + 1}";

    /// <summary>Header of a chain segment.</summary>
    public string Ordinal => $"SECTION {Index + 1}";

    /// <summary>Second line of a chain segment.</summary>
    public string LayerRangeText => HasKnownRange ? $"layers {FirstLayer}-{LastLayer}" : "layers not reported";
}
