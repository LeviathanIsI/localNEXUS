using System.IO;
using System.Text.Json;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Decides what a model on disk is by reading it, never by trusting its name.
/// </summary>
/// <remarks>
/// This is the only place in the application that answers the question. An extension is a
/// convention a user can break in a second, and the failure mode of trusting one is handing a
/// file to a runtime that cannot read it and reporting whatever confusing error comes back.
/// Reading the first few bytes costs nothing by comparison.
/// </remarks>
public static class ModelFormatDetector
{
    /// <summary>The four bytes every GGUF file starts with.</summary>
    private static readonly byte[] GgufMagic = { 0x47, 0x47, 0x55, 0x46 };

    /// <summary>
    /// A safetensors file starts with an eight byte little endian header length. Anything beyond
    /// this is a corrupt or unrelated file rather than a header worth reading.
    /// </summary>
    private const long MaxSafetensorsHeaderBytes = 100L * 1024 * 1024;

    /// <summary>How far below a search folder the scan looks for models.</summary>
    private const int MaxScanDepth = 4;

    /// <summary>
    /// Describes whatever is at the given path. Never throws: an unreadable path is a described
    /// state, because a model on a disconnected drive should be reported, not crash a scan.
    /// </summary>
    public static ModelDescriptor Describe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Unknown(string.Empty, string.Empty, "No path was given.");
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unknown(path, path, "That is not a usable path.");
        }

        if (Directory.Exists(full))
        {
            return DescribeDirectory(full);
        }

        if (File.Exists(full))
        {
            return DescribeFile(full);
        }

        return Unknown(full, NameOf(full), "Nothing is at that path any more.");
    }

    private static ModelDescriptor DescribeDirectory(string folder)
    {
        var name = new DirectoryInfo(folder).Name;

        string[] weights;
        try
        {
            weights = Directory.GetFiles(folder, "*.safetensors", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Unknown(folder, name, $"That folder could not be read: {ex.Message}");
        }

        if (weights.Length == 0)
        {
            return Unknown(folder, name, "That folder holds no safetensors weight files.");
        }

        var size = TotalSize(weights);
        var configPath = Path.Combine(folder, "config.json");

        if (!File.Exists(configPath))
        {
            // Weights with no configuration beside them describe a piece of a model, not a model.
            // Saying so is more useful than loading it and failing somewhere inside the runtime.
            return new ModelDescriptor(
                folder,
                ModelFormat.SafetensorsComponent,
                name,
                size,
                $"{weights.Length} weight file(s)",
                "There is no config.json beside these weights, so this is a component of a model rather than a model that can be served.");
        }

        var architecture = ReadArchitecture(configPath);
        var detail = architecture is null
            ? $"{weights.Length} weight file(s)"
            : $"{architecture}, {weights.Length} weight file(s)";

        return new ModelDescriptor(folder, ModelFormat.Safetensors, name, size, detail);
    }

    private static ModelDescriptor DescribeFile(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var size = SizeOf(file);

        byte[] head;
        try
        {
            head = ReadHead(file, 16);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Unknown(file, name, $"That file could not be read: {ex.Message}");
        }

        if (StartsWithGgufMagic(head))
        {
            var version = head.Length >= 8 ? BitConverter.ToUInt32(head, 4) : 0u;
            return new ModelDescriptor(file, ModelFormat.Gguf, name, size, version == 0 ? null : $"GGUF v{version}");
        }

        if (LooksLikeSafetensors(head, size))
        {
            // A lone safetensors file carries no configuration and so cannot be served on its
            // own. What would be servable is the folder around it, and this is not one.
            var folder = Path.GetDirectoryName(file);
            var hint = folder is not null && File.Exists(Path.Combine(folder, "config.json"))
                ? $"Select the folder {folder} instead, which holds the config.json this file belongs to."
                : "A servable safetensors model is a folder holding config.json alongside its weight files.";

            return new ModelDescriptor(
                file,
                ModelFormat.SafetensorsComponent,
                Path.GetFileName(file),
                size,
                null,
                $"This is a single safetensors file rather than a complete model. {hint}");
        }

        return Unknown(file, name, "That file is neither a GGUF nor safetensors weights.");
    }

    private static bool StartsWithGgufMagic(byte[] head)
        => head.Length >= GgufMagic.Length
           && head[0] == GgufMagic[0]
           && head[1] == GgufMagic[1]
           && head[2] == GgufMagic[2]
           && head[3] == GgufMagic[3];

    /// <summary>
    /// Safetensors has no magic number. What it does have is an eight byte header length
    /// followed by exactly that many bytes of JSON, so a plausible length and an opening brace
    /// together are as close to a signature as the format offers.
    /// </summary>
    private static bool LooksLikeSafetensors(byte[] head, long fileSize)
    {
        if (head.Length < 9 || fileSize <= 8)
        {
            return false;
        }

        var headerLength = (long)BitConverter.ToUInt64(head, 0);

        if (headerLength <= 0 || headerLength > MaxSafetensorsHeaderBytes || headerLength > fileSize - 8)
        {
            return false;
        }

        return head[8] == OpeningBrace;
    }

    private const byte OpeningBrace = 0x7B;

    private static string? ReadArchitecture(string configPath)
    {
        try
        {
            using var stream = File.OpenRead(configPath);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;

            if (root.TryGetProperty("architectures", out var architectures)
                && architectures.ValueKind == JsonValueKind.Array
                && architectures.GetArrayLength() > 0
                && architectures[0].GetString() is { Length: > 0 } first)
            {
                return first;
            }

            if (root.TryGetProperty("model_type", out var modelType) && modelType.GetString() is { Length: > 0 } type)
            {
                return type;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A config that will not parse is not worth failing detection over. The folder still
            // holds weights and a config, which is what makes it a safetensors model.
        }

        return null;
    }

    private static byte[] ReadHead(string file, int count)
    {
        using var stream = File.OpenRead(file);
        var buffer = new byte[count];
        var read = stream.Read(buffer, 0, count);
        return read == count ? buffer : buffer.AsSpan(0, read).ToArray();
    }

    private static long SizeOf(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long TotalSize(IEnumerable<string> files)
    {
        long total = 0;
        foreach (var file in files)
        {
            total += SizeOf(file);
        }

        return total;
    }

    private static string NameOf(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static ModelDescriptor Unknown(string path, string name, string reason)
        => new(path, ModelFormat.Unknown, name, 0, null, reason);

    /// <summary>
    /// Every folder worth looking inside when scanning for models, to the depth the catalogue
    /// searches. Kept here so the catalogue and any later caller agree on what a scan covers.
    /// </summary>
    public static IEnumerable<string> EnumerateCandidateDirectories(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            yield return current;

            if (depth >= MaxScanDepth)
            {
                continue;
            }

            string[] children;
            try
            {
                children = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var child in children)
            {
                queue.Enqueue((child, depth + 1));
            }
        }
    }
}
