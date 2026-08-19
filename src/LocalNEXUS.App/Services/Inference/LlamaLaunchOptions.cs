namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// The per launch settings of a llama-server process.
/// </summary>
/// <remarks>
/// Two launches of the same GGUF with different options are different servers, so these values
/// are part of the key the manager tracks servers under. The defaults reproduce the behaviour
/// the application shipped with: an 8192 token context and every layer offloaded to the GPU.
/// </remarks>
public sealed record LlamaLaunchOptions
{
    /// <summary>Context window used when a node does not override it.</summary>
    public const int DefaultContextSize = 8192;

    /// <summary>
    /// Default GPU layer count. Deliberately larger than any real model's layer count, which
    /// llama.cpp treats as "offload everything".
    /// </summary>
    public const int DefaultGpuLayers = 999;

    /// <summary>Context window passed to the server with <c>-c</c>.</summary>
    public int ContextSize { get; init; } = DefaultContextSize;

    /// <summary>Layers offloaded to the GPU, passed with <c>-ngl</c>.</summary>
    public int GpuLayers { get; init; } = DefaultGpuLayers;

    /// <summary>
    /// The key a server started with these options is tracked under. The model path is part of
    /// the key so one entry exists per model and configuration pair.
    /// </summary>
    public string BuildServerKey(string fullModelPath)
        => $"{fullModelPath}|c{ContextSize}|ngl{GpuLayers}";
}
