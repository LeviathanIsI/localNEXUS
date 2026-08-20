using System.IO;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// What a model is, independent of any runtime that might serve it.
/// </summary>
/// <remarks>
/// Detection is expensive enough to be worth doing once and central enough to be worth doing in
/// one place, so everything downstream, the catalogue, the resolver and the panels, reads this
/// record rather than looking at the path again. Nothing here knows about llama.cpp or Python.
/// </remarks>
public sealed class ModelDescriptor
{
    public ModelDescriptor(
        string path,
        ModelFormat format,
        string displayName,
        long sizeBytes,
        string? detail = null,
        string? unsupportedReason = null)
    {
        Path = path;
        Format = format;
        DisplayName = displayName;
        SizeBytes = sizeBytes;
        Detail = detail;
        UnsupportedReason = unsupportedReason;
    }

    /// <summary>Absolute path of the file or folder this describes.</summary>
    public string Path { get; }

    /// <summary>What the content says this is.</summary>
    public ModelFormat Format { get; }

    /// <summary>Name shown to the user: the file name, or the folder name for a repository.</summary>
    public string DisplayName { get; }

    /// <summary>Bytes on disk. For a folder, the total of its weight files.</summary>
    public long SizeBytes { get; }

    /// <summary>Whatever detection could cheaply read, for example the model architecture.</summary>
    public string? Detail { get; }

    /// <summary>Why this cannot be served, when it cannot. Null when it can.</summary>
    public string? UnsupportedReason { get; }

    /// <summary>True when some runtime could be expected to serve this.</summary>
    public bool IsServable => Format is ModelFormat.Gguf or ModelFormat.Safetensors;

    /// <summary>The format as a short label, which is information rather than a choice.</summary>
    public string FormatLabel => Format switch
    {
        ModelFormat.Gguf => "GGUF",
        ModelFormat.Safetensors => "safetensors",
        ModelFormat.SafetensorsComponent => "safetensors component",
        _ => "unrecognised"
    };

    /// <summary>
    /// The quantization this model was built at, or "not stated" when the name does not say.
    /// </summary>
    /// <remarks>
    /// Read from the name rather than from the file. A GGUF header does carry the type, but
    /// reading it means opening and parsing every model in the catalogue to fill in a label, and
    /// the naming convention is close to universal. Where the convention was not followed this
    /// says so instead of guessing, which is the same rule format detection follows: report what
    /// is known, never invent what is not.
    /// </remarks>
    public string Quantisation => QuantisationLabel.Read(DisplayName);

    /// <summary>Size on disk in GB, or zero when it could not be read.</summary>
    public double SizeGb => SizeBytes / 1024d / 1024d / 1024d;

    /// <summary>Size as the panels show it.</summary>
    public string SizeLabel => SizeBytes > 0 ? $"{SizeGb:0.0} GB" : "size unknown";

    /// <summary>Name, size and format, as shown in the dropdown.</summary>
    public string CatalogLabel => SizeBytes > 0
        ? $"{DisplayName}  ({SizeBytes / 1024d / 1024d / 1024d:0.0} GB, {FormatLabel})"
        : $"{DisplayName}  ({FormatLabel})";

    /// <summary>True when the path this describes is still there.</summary>
    public bool Exists => File.Exists(Path) || Directory.Exists(Path);

    public override string ToString() => CatalogLabel;
}
