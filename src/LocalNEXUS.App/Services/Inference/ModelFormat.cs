namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// What a local model on disk actually is, as decided by looking inside it.
/// </summary>
/// <remarks>
/// Format is worked out from content rather than from a file extension or a folder name,
/// because both are things a user renames. A file called <c>model.bin</c> that begins with the
/// GGUF magic is a GGUF, and a folder full of <c>.safetensors</c> shards with no
/// <c>config.json</c> beside them is not a model at all. The unrecognised cases are values of
/// their own so the catalogue can say what it found instead of guessing.
/// </remarks>
public enum ModelFormat
{
    /// <summary>Nothing recognisable. The reason is carried on the descriptor.</summary>
    Unknown,

    /// <summary>A single GGUF file, identified by its magic bytes.</summary>
    Gguf,

    /// <summary>A folder holding a <c>config.json</c> and one or more <c>.safetensors</c> files.</summary>
    Safetensors,

    /// <summary>
    /// Safetensors weights with no model configuration beside them. Usually a component of a
    /// larger pipeline, for example a LoRA or a VAE, rather than something that can be served.
    /// </summary>
    SafetensorsComponent
}
