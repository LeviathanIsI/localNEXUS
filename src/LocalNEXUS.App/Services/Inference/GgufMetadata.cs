using System.IO;
using System.Text;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// The few things worth knowing from a GGUF file's key and value header.
/// </summary>
/// <param name="Version">The container version, which decides how lengths are written.</param>
/// <param name="Architecture">What <c>general.architecture</c> says, when it says anything.</param>
/// <param name="HasVisionEncoder">
/// True when the file declares <c>clip.has_vision_encoder</c>, which is what makes it a
/// multimodal projector rather than a model.
/// </param>
/// <param name="ProjectorType">The projector kind, for example <c>mlp</c> or <c>qwen2vl_merger</c>.</param>
public sealed record GgufHeader(uint Version, string? Architecture, bool HasVisionEncoder, string? ProjectorType);

/// <summary>
/// Reads the metadata block at the front of a GGUF file.
/// </summary>
/// <remarks>
/// This exists because a multimodal projector has to be told apart from a model, and the only
/// honest way to do that is to read what the file says about itself. The convention people follow
/// is a file named <c>mmproj-something.gguf</c> beside the weights, but a name is a thing anybody
/// can change, and format detection already refuses to trust one. What is inside is definite:
/// llama.cpp's own loader decides a file is a vision projector by looking for the boolean
/// <c>clip.has_vision_encoder</c>, and nothing but a projector carries it.
///
/// Only the header is read. A GGUF puts every key and value before the first tensor, so the answer
/// is in the first few kilobytes of a projector, and a scan budget stops a large model's tokenizer
/// array from turning a question into a file read. A model whose metadata is larger than the budget
/// is reported as having no vision encoder, which is correct: a projector's metadata is small.
///
/// Never throws. An unreadable or truncated file is a null answer, the same way an unreadable path
/// is a described state in <see cref="ModelFormatDetector"/> rather than a crashed scan.
/// </remarks>
public static class GgufMetadata
{
    /// <summary>The four bytes every GGUF file starts with.</summary>
    private static readonly byte[] Magic = { 0x47, 0x47, 0x55, 0x46 };

    /// <summary>
    /// The oldest container this reads. Version 1 wrote its lengths as 32 bit and llama.cpp itself
    /// stopped loading it long ago, so refusing it is more honest than misparsing it.
    /// </summary>
    public const uint MinimumVersion = 2;

    /// <summary>How far into a file the metadata scan will go before giving up.</summary>
    /// <remarks>
    /// A projector's whole header is a few kilobytes. A large language model's is a few megabytes,
    /// almost all of it the tokenizer vocabulary, and reading all of that to learn it is not a
    /// projector is work for nothing. The budget is generous enough that no real projector reaches
    /// it and small enough that probing a folder of models stays quick.
    /// </remarks>
    public const long ScanBudgetBytes = 4L * 1024 * 1024;

    /// <summary>Refuses a header claiming more pairs than any real file has.</summary>
    private const ulong MaxKeyValuePairs = 1_000_000;

    /// <summary>The longest key or value string that is read into memory rather than skipped.</summary>
    private const ulong MaxStringBytes = 64 * 1024;

    private const string ArchitectureKey = "general.architecture";
    private const string VisionEncoderKey = "clip.has_vision_encoder";
    private const string ProjectorTypeKey = "clip.projector_type";
    private const string VisionProjectorTypeKey = "clip.vision.projector_type";

    /// <summary>
    /// Reads what the header says, or null when the path is not a readable GGUF.
    /// </summary>
    public static GgufHeader? Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            return ReadHeader(reader, stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException
                                       or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>True when this file is a multimodal projector that can see.</summary>
    public static bool IsVisionProjector(string path) => Read(path)?.HasVisionEncoder == true;

    private static GgufHeader? ReadHeader(BinaryReader reader, Stream stream)
    {
        var magic = reader.ReadBytes(Magic.Length);

        if (magic.Length != Magic.Length
            || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
        {
            return null;
        }

        var version = reader.ReadUInt32();

        if (version < MinimumVersion)
        {
            return null;
        }

        // Tensor count, which nothing here needs but which sits between the version and the pairs.
        _ = reader.ReadUInt64();

        var pairs = reader.ReadUInt64();

        if (pairs > MaxKeyValuePairs)
        {
            return null;
        }

        string? architecture = null;
        string? projectorType = null;
        var hasVisionEncoder = false;

        for (ulong i = 0; i < pairs; i++)
        {
            if (stream.Position >= ScanBudgetBytes)
            {
                // Out of budget. Whatever was found so far is reported, which for a model too
                // large to finish reading is that it declares no vision encoder.
                break;
            }

            var key = ReadKey(reader);

            if (key is null)
            {
                break;
            }

            var type = (GgufValueType)reader.ReadUInt32();

            switch (key)
            {
                case VisionEncoderKey when type == GgufValueType.Bool:
                    hasVisionEncoder = reader.ReadByte() != 0;
                    break;

                case ArchitectureKey when type == GgufValueType.String:
                    architecture = ReadString(reader);
                    break;

                case ProjectorTypeKey or VisionProjectorTypeKey when type == GgufValueType.String:
                    projectorType = ReadString(reader) ?? projectorType;
                    break;

                default:
                    if (!Skip(reader, stream, type))
                    {
                        return Done(version, architecture, hasVisionEncoder, projectorType);
                    }

                    break;
            }
        }

        return Done(version, architecture, hasVisionEncoder, projectorType);
    }

    private static GgufHeader Done(uint version, string? architecture, bool hasVisionEncoder, string? projectorType)
        => new(version, architecture, hasVisionEncoder, projectorType);

    /// <summary>A key, or null when it is implausibly long and the file is not worth trusting.</summary>
    private static string? ReadKey(BinaryReader reader)
    {
        var length = reader.ReadUInt64();

        if (length > MaxStringBytes)
        {
            return null;
        }

        return Encoding.UTF8.GetString(reader.ReadBytes((int)length));
    }

    /// <summary>A string value, or null when it is too long to be one of the ones wanted.</summary>
    private static string? ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt64();

        if (length > MaxStringBytes)
        {
            reader.BaseStream.Seek((long)length, SeekOrigin.Current);
            return null;
        }

        return Encoding.UTF8.GetString(reader.ReadBytes((int)length));
    }

    /// <summary>
    /// Steps over a value of a type nothing here reads. False when the type is not one this
    /// version of the format defines, which means the rest of the header cannot be trusted.
    /// </summary>
    private static bool Skip(BinaryReader reader, Stream stream, GgufValueType type)
    {
        if (type == GgufValueType.String)
        {
            var length = reader.ReadUInt64();
            stream.Seek((long)length, SeekOrigin.Current);
            return true;
        }

        if (type == GgufValueType.Array)
        {
            var elementType = (GgufValueType)reader.ReadUInt32();
            var count = reader.ReadUInt64();

            if (elementType == GgufValueType.Array)
            {
                // Nested arrays are legal in the specification and produced by nothing, and
                // stepping over one correctly would mean walking every element. Stopping here is
                // the honest answer rather than a seek to a guessed offset.
                return false;
            }

            if (elementType == GgufValueType.String)
            {
                for (ulong i = 0; i < count; i++)
                {
                    if (stream.Position >= ScanBudgetBytes)
                    {
                        return false;
                    }

                    var length = reader.ReadUInt64();
                    stream.Seek((long)length, SeekOrigin.Current);
                }

                return true;
            }

            if (SizeOf(elementType) is not { } elementSize)
            {
                return false;
            }

            stream.Seek((long)count * elementSize, SeekOrigin.Current);
            return true;
        }

        if (SizeOf(type) is not { } size)
        {
            return false;
        }

        stream.Seek(size, SeekOrigin.Current);
        return true;
    }

    private static int? SizeOf(GgufValueType type) => type switch
    {
        GgufValueType.UInt8 or GgufValueType.Int8 or GgufValueType.Bool => 1,
        GgufValueType.UInt16 or GgufValueType.Int16 => 2,
        GgufValueType.UInt32 or GgufValueType.Int32 or GgufValueType.Float32 => 4,
        GgufValueType.UInt64 or GgufValueType.Int64 or GgufValueType.Float64 => 8,
        _ => null
    };

    /// <summary>The value types a GGUF header can hold, numbered as the format numbers them.</summary>
    private enum GgufValueType : uint
    {
        UInt8 = 0,
        Int8 = 1,
        UInt16 = 2,
        Int16 = 3,
        UInt32 = 4,
        Int32 = 5,
        Float32 = 6,
        Bool = 7,
        String = 8,
        Array = 9,
        UInt64 = 10,
        Int64 = 11,
        Float64 = 12
    }
}
