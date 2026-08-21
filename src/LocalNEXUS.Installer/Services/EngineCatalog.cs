using LocalNEXUS.Installer.Models;

namespace LocalNEXUS.Installer.Services;

/// <summary>
/// The pinned engine releases, their sizes and their hashes.
/// </summary>
/// <remarks>
/// Nothing here is redistributed. Each asset is fetched from its own release page at install
/// time, so llama.cpp stays MIT under the ggml authors, and Mesh LLM and uv stay Apache-2.0 under
/// theirs. That is the reason the installer is 55 MB rather than 700.
///
/// Every version is pinned rather than tracking latest, for two different reasons.
///
/// llama.cpp has no choice in the matter: its asset names carry the build number, so
/// releases/latest/download cannot name a file that exists.
///
/// Mesh LLM and uv both publish unversioned asset names that a latest url would resolve, and are
/// pinned anyway, because a pinned url is the only one whose hash can be stated in advance. An
/// unverified engine binary that arrived truncated produces failures that look exactly like
/// application bugs, and chasing one of those costs more than a version bump ever will.
///
/// Every hash below came from the GitHub release API rather than from hashing a local download,
/// so a machine that was already compromised could not have laundered a bad file into this list.
///
/// Bumping a version: change the constants, and take the size and digest for each asset from
/// the release API rather than from a local file.
/// </remarks>
public static class EngineCatalog
{
    /// <summary>The llama.cpp build every flavour below comes from.</summary>
    public const string LlamaBuild = "b10549";

    /// <summary>The Mesh LLM release.</summary>
    public const string MeshVersion = "v0.75.1";

    /// <summary>The uv release.</summary>
    public const string UvVersion = "0.12.5";

    private const string LlamaBase = "https://github.com/ggml-org/llama.cpp/releases/download/" + LlamaBuild + "/";
    private const string MeshBase = "https://github.com/Mesh-LLM/mesh-llm/releases/download/" + MeshVersion + "/";
    private const string UvBase = "https://github.com/astral-sh/uv/releases/download/" + UvVersion + "/";

    /// <summary>
    /// Everything a given llama.cpp flavour needs.
    /// </summary>
    /// <remarks>
    /// The CUDA flavours are two files. The build itself does not carry the CUDA runtime, and a
    /// build without its matching runtime starts and then fails to find a GPU, which is the
    /// least helpful failure available.
    /// </remarks>
    public static IReadOnlyList<EngineAsset> Llama(LlamaFlavour flavour) => flavour switch
    {
        LlamaFlavour.Cuda13 => new[]
        {
            new EngineAsset(
                "llama.cpp (CUDA 13)",
                "llama.zip",
                LlamaBase + "llama-" + LlamaBuild + "-bin-win-cuda-13.3-x64.zip",
                "67a1097716a4b4c20b94d248d1b3886fd7b91b73d9af5e0630fd6a25a32309a5",
                146_945_631L,
                "llama"),
            new EngineAsset(
                "CUDA runtime",
                "cudart.zip",
                LlamaBase + "cudart-llama-bin-win-cuda-13.3-x64.zip",
                "1462a050eb4c684921ba51dcc4cc488a036674c3e73e9945ee705b854808d03e",
                390_970_417L,
                "llama")
        },

        LlamaFlavour.Cuda12 => new[]
        {
            new EngineAsset(
                "llama.cpp (CUDA 12)",
                "llama.zip",
                LlamaBase + "llama-" + LlamaBuild + "-bin-win-cuda-12.4-x64.zip",
                "2e980ae28b40c92c9c30bdbcf3f28064b40104472e213c52edbeb89b920d65fe",
                250_969_968L,
                "llama"),
            new EngineAsset(
                "CUDA runtime",
                "cudart.zip",
                LlamaBase + "cudart-llama-bin-win-cuda-12.4-x64.zip",
                "8c79a9b226de4b3cacfd1f83d24f962d0773be79f1e7b75c6af4ded7e32ae1d6",
                391_443_627L,
                "llama")
        },

        LlamaFlavour.Vulkan => new[]
        {
            new EngineAsset(
                "llama.cpp (Vulkan)",
                "llama.zip",
                LlamaBase + "llama-" + LlamaBuild + "-bin-win-vulkan-x64.zip",
                "8e7b0e6382a5bcbf57c79cf54b61483e9f7b26561d4413f28095cdaee256207b",
                34_936_498L,
                "llama")
        },

        _ => new[]
        {
            new EngineAsset(
                "llama.cpp (processor only)",
                "llama.zip",
                LlamaBase + "llama-" + LlamaBuild + "-bin-win-cpu-x64.zip",
                "11d38f2ed878489b2c3d02b3d1a67683c02fbfb3d265876b9ede749a8dff5f1c",
                18_581_129L,
                "llama")
        }
    };

    /// <summary>
    /// Mesh LLM, Vulkan only.
    /// </summary>
    /// <remarks>
    /// No flavour choice here, deliberately. The CUDA bundle is 824 MB against this 50 MB, and
    /// on a CUDA 13 era driver it has been seen to report zero GPUs and fall back to the
    /// processor anyway, so the large download buys nothing and can cost correctness. There is
    /// no decision to get wrong.
    /// </remarks>
    public static EngineAsset Mesh { get; } = new(
        "Mesh LLM (Vulkan)",
        "mesh.zip",
        MeshBase + "mesh-llm-" + MeshVersion + "-x86_64-pc-windows-msvc-vulkan.zip",
        "92ecb0bef7678651264d35d8f41ae210e82ca78dcfba796ee4df50cd75776ff2",
        53_220_543L,
        "mesh");

    /// <summary>uv, which builds the Python runtime that serves safetensors models.</summary>
    public static EngineAsset Uv { get; } = new(
        "uv",
        "uv.zip",
        UvBase + "uv-x86_64-pc-windows-msvc.zip",
        "4c4d49d8738847d9b71ba319e49a5688c93eac0fe6204b1df24e98528dddf39a",
        20_329_591L,
        "uv");

    /// <summary>The size a flavour totals, for the line on the build page.</summary>
    public static long LlamaBytes(LlamaFlavour flavour) => Llama(flavour).Sum(a => a.Bytes);
}
