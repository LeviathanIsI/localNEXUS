using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>One local model discovered on disk, as offered in a model node's dropdown.</summary>
/// <remarks>
/// A GGUF file and a safetensors folder are both one entry here. The format is carried as a
/// label rather than as a filter, because which runtime serves a model is the application's
/// problem and not something a user should have to answer before picking one.
/// </remarks>
public sealed class LocalModelInfo
{
    public LocalModelInfo(ModelDescriptor descriptor) => Descriptor = descriptor;

    /// <summary>What detection found when it looked inside this path.</summary>
    public ModelDescriptor Descriptor { get; }

    /// <summary>Absolute path of the model file or folder.</summary>
    public string Path => Descriptor.Path;

    /// <summary>Display name: the file name, or the folder name for a safetensors model.</summary>
    public string Name => Descriptor.DisplayName;

    /// <summary>Size on disk in bytes, or zero when it could not be read.</summary>
    public long SizeBytes => Descriptor.SizeBytes;

    /// <summary>The detected format, shown as information beside the name.</summary>
    public ModelFormat Format => Descriptor.Format;

    /// <summary>The format as a short label.</summary>
    public string FormatLabel => Descriptor.FormatLabel;

    /// <summary>Name, size and format, as shown in the dropdown.</summary>
    public string DisplayName => Descriptor.CatalogLabel;

    public override string ToString() => DisplayName;
}
