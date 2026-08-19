using System.IO;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>One GGUF file discovered on disk, as offered in a model node's dropdown.</summary>
public sealed class LocalModelInfo
{
    public LocalModelInfo(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);

        try
        {
            SizeBytes = new FileInfo(path).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SizeBytes = 0;
        }
    }

    /// <summary>Absolute path of the GGUF file.</summary>
    public string Path { get; }

    /// <summary>File name without the extension, used as the display name.</summary>
    public string Name { get; }

    /// <summary>Size on disk in bytes, or zero when it could not be read.</summary>
    public long SizeBytes { get; }

    /// <summary>Name and size, as shown in the dropdown.</summary>
    public string DisplayName => SizeBytes > 0
        ? $"{Name}  ({SizeBytes / 1024d / 1024d / 1024d:0.0} GB)"
        : Name;

    public override string ToString() => DisplayName;
}
