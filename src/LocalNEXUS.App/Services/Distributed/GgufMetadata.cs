using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Minimal reader for the GGUF header, enough to learn a model's name, architecture and layer
/// count without loading the file. Only the metadata key value section is walked; tensor data
/// is never touched.
/// </summary>
public static partial class GgufMetadata
{
    private const uint Magic = 0x46554747; // "GGUF" little endian
    private const int MaxKeyLength = 65536;
    private const int MaxStringLength = 16 * 1024 * 1024;

    /// <summary>
    /// Reads the header of a GGUF file.
    /// </summary>
    /// <exception cref="InvalidDataException">The file is not a readable GGUF of a supported version.</exception>
    public static GgufModelInfo Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        if (reader.ReadUInt32() != Magic)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not a GGUF file.");
        }

        var version = reader.ReadUInt32();
        if (version is < 2 or > 3)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} uses GGUF version {version}, which this reader does not understand.");
        }

        _ = reader.ReadUInt64(); // tensor count, not needed
        var kvCount = reader.ReadUInt64();

        var strings = new Dictionary<string, string>(StringComparer.Ordinal);
        var integers = new Dictionary<string, long>(StringComparer.Ordinal);

        for (ulong i = 0; i < kvCount; i++)
        {
            var key = ReadString(reader, MaxKeyLength);
            var valueType = reader.ReadUInt32();
            ReadValue(reader, valueType, key, strings, integers);
        }

        if (!strings.TryGetValue("general.architecture", out var architecture))
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} has no general.architecture key, so its layer count cannot be found.");
        }

        if (!integers.TryGetValue($"{architecture}.block_count", out var blockCount) || blockCount <= 0)
        {
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} has no {architecture}.block_count key, so its layer count cannot be found.");
        }

        var name = strings.TryGetValue("general.name", out var metadataName) && !string.IsNullOrWhiteSpace(metadataName)
            ? metadataName
            : Path.GetFileNameWithoutExtension(path);

        return new GgufModelInfo(
            name,
            architecture,
            (int)blockCount,
            DeriveQuantization(path),
            new FileInfo(path).Length);
    }

    /// <summary>
    /// The quantization label, taken from the conventional file name suffix because the header
    /// stores it only as a numeric enum whose values shift between llama.cpp releases.
    /// </summary>
    private static string DeriveQuantization(string path)
    {
        var match = QuantizationPattern().Match(Path.GetFileNameWithoutExtension(path));
        return match.Success ? match.Value.ToUpperInvariant() : "unknown";
    }

    [GeneratedRegex(@"(?i)(i?q\d[_a-z0-9]*|f16|f32|bf16)(?![a-z0-9])")]
    private static partial Regex QuantizationPattern();

    private static void ReadValue(
        BinaryReader reader,
        uint valueType,
        string key,
        Dictionary<string, string> strings,
        Dictionary<string, long> integers)
    {
        switch (valueType)
        {
            case 0: integers[key] = reader.ReadByte(); break;
            case 1: integers[key] = reader.ReadSByte(); break;
            case 2: integers[key] = reader.ReadUInt16(); break;
            case 3: integers[key] = reader.ReadInt16(); break;
            case 4: integers[key] = reader.ReadUInt32(); break;
            case 5: integers[key] = reader.ReadInt32(); break;
            case 6: _ = reader.ReadSingle(); break;
            case 7: integers[key] = reader.ReadByte(); break;
            case 8: strings[key] = ReadString(reader, MaxStringLength); break;
            case 9: SkipArray(reader); break;
            case 10: integers[key] = unchecked((long)reader.ReadUInt64()); break;
            case 11: integers[key] = reader.ReadInt64(); break;
            case 12: _ = reader.ReadDouble(); break;
            default:
                throw new InvalidDataException($"GGUF metadata key {key} has unknown value type {valueType}.");
        }
    }

    private static void SkipArray(BinaryReader reader)
    {
        var elementType = reader.ReadUInt32();
        var count = reader.ReadUInt64();

        var fixedSize = elementType switch
        {
            0 or 1 or 7 => 1,
            2 or 3 => 2,
            4 or 5 or 6 => 4,
            10 or 11 or 12 => 8,
            _ => 0
        };

        if (fixedSize > 0)
        {
            reader.BaseStream.Seek((long)count * fixedSize, SeekOrigin.Current);
            return;
        }

        if (elementType == 8)
        {
            for (ulong i = 0; i < count; i++)
            {
                var length = reader.ReadUInt64();
                if (length > MaxStringLength)
                {
                    throw new InvalidDataException("GGUF metadata contains an implausibly long string.");
                }

                reader.BaseStream.Seek((long)length, SeekOrigin.Current);
            }

            return;
        }

        throw new InvalidDataException($"GGUF metadata contains an array of unknown element type {elementType}.");
    }

    private static string ReadString(BinaryReader reader, int maxLength)
    {
        var length = reader.ReadUInt64();
        if (length > (ulong)maxLength)
        {
            throw new InvalidDataException("GGUF metadata contains an implausibly long string.");
        }

        var bytes = reader.ReadBytes((int)length);
        if (bytes.Length != (int)length)
        {
            throw new InvalidDataException("GGUF metadata ended unexpectedly.");
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
