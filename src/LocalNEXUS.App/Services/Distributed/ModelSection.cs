namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// One slot in a split model: a contiguous range of layers that some source has to hold for
/// the pipeline to exist. Sections are the core concept of distributed inference here; peers
/// and machines are just whatever happens to fill them.
/// </summary>
/// <param name="Index">Position of this section in the pipeline, starting at zero.</param>
/// <param name="ModelName">The model's own name from its metadata, not a local file path.</param>
/// <param name="Quantization">Quantization label, part of the section's identity.</param>
/// <param name="FirstLayer">First transformer layer this section covers, inclusive.</param>
/// <param name="LastLayer">Last transformer layer this section covers, inclusive.</param>
public sealed record ModelSection(
    int Index,
    string ModelName,
    string Quantization,
    int FirstLayer,
    int LastLayer)
{
    /// <summary>Number of layers in this section.</summary>
    public int LayerCount => LastLayer - FirstLayer + 1;

    /// <summary>Short label for the panel and for refusal messages.</summary>
    public string Label => $"section {Index + 1} (layers {FirstLayer}-{LastLayer})";

    /// <summary>Header of a chain segment.</summary>
    public string Ordinal => $"SECTION {Index + 1}";

    /// <summary>Second line of a chain segment.</summary>
    public string LayerRangeText => $"layers {FirstLayer}-{LastLayer}";
}
