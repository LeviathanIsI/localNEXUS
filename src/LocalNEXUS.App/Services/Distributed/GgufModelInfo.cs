namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// The facts about a GGUF file that coverage planning needs: identity for sections, layer
/// count for the split, and size for the memory arithmetic.
/// </summary>
/// <param name="Name">The model's own name from its metadata, falling back to the file name.</param>
/// <param name="Architecture">The architecture key from the metadata, for example <c>qwen2</c>.</param>
/// <param name="LayerCount">Number of transformer layers, from the architecture's block count.</param>
/// <param name="Quantization">Quantization label derived from the file name, or <c>unknown</c>.</param>
/// <param name="FileBytes">Size of the file on disk, the basis of the memory estimate.</param>
public sealed record GgufModelInfo(
    string Name,
    string Architecture,
    int LayerCount,
    string Quantization,
    long FileBytes)
{
    /// <summary>
    /// Estimated memory needed to serve the whole model, in MiB. Weights plus a flat allowance
    /// for compute and KV buffers. An estimate, deliberately conservative rather than exact.
    /// </summary>
    public long EstimatedMemoryMb => (long)(FileBytes * 1.2d / (1024 * 1024)) + 512;
}
