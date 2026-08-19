using System.Globalization;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// The per launch settings of a llama-server process.
/// </summary>
/// <remarks>
/// Two launches of the same GGUF with different options are different servers, so these values
/// are part of the key the manager tracks servers under. That includes the topology: the same
/// model served alone on this machine and split across sources are different servers. The
/// defaults reproduce the behaviour the application shipped with: an 8192 token context and
/// every layer offloaded to the GPU.
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
    /// The rpc-server endpoints this launch fans out to, as host:port strings in the order the
    /// coverage plan assigned them. Empty means a plain local launch. llama.cpp registers these
    /// as devices ahead of the local GPU, in list order.
    /// </summary>
    public IReadOnlyList<string> RpcEndpoints { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Proportions for <c>-ts</c>, in device order: one per RPC endpoint first, the local GPU
    /// last. Empty lets llama.cpp divide by device memory on its own.
    /// </summary>
    public IReadOnlyList<double> TensorSplit { get; init; } = Array.Empty<double>();

    /// <summary>True when this launch spans rpc backends.</summary>
    public bool IsDistributed => RpcEndpoints.Count > 0;

    /// <summary>
    /// The key a server started with these options is tracked under. The model path and the
    /// topology are both part of the key, so one entry exists per model and configuration pair.
    /// </summary>
    public string BuildServerKey(string fullModelPath)
    {
        var key = $"{fullModelPath}|c{ContextSize}|ngl{GpuLayers}";

        if (IsDistributed)
        {
            var split = string.Join(",", TensorSplit.Select(p => p.ToString("0.####", CultureInfo.InvariantCulture)));
            key += $"|rpc:{string.Join(",", RpcEndpoints)}|ts:{split}";
        }

        return key;
    }
}
