namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// The per launch settings a node hands to whichever runtime serves its model.
/// </summary>
/// <remarks>
/// Deliberately the two settings a model node already exposes rather than a union of everything
/// every runtime accepts. A runtime that has no use for one of them ignores it, which is a
/// smaller cost than a settings panel whose fields change meaning with the file that was picked.
/// </remarks>
public sealed record ModelRuntimeOptions
{
    /// <summary>Context window requested. Passed straight through to llama-server.</summary>
    public int ContextSize { get; init; } = LlamaLaunchOptions.DefaultContextSize;

    /// <summary>Layers to offload to the GPU. Meaningful to llama.cpp; the Python runtime places the whole model.</summary>
    public int GpuLayers { get; init; } = LlamaLaunchOptions.DefaultGpuLayers;

    /// <summary>The llama.cpp shaped view of these options.</summary>
    public LlamaLaunchOptions ToLlamaLaunchOptions()
        => new() { ContextSize = ContextSize, GpuLayers = GpuLayers };
}
