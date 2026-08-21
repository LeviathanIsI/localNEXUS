namespace LocalNEXUS.Installer.Models;

/// <summary>
/// One file to fetch: where it comes from, what it should hash to, and where it is unpacked.
/// </summary>
/// <param name="Label">What the fetch list and the progress line call it.</param>
/// <param name="FileName">Name it is saved under while downloading.</param>
/// <param name="Url">Pinned release asset url.</param>
/// <param name="Sha256">Expected hash, lower case hex, taken from the release API rather than from hashing a local copy.</param>
/// <param name="Bytes">Exact size, so the fetch list can state it before anything is downloaded.</param>
/// <param name="VendorFolder">Which folder under vendor it unpacks into.</param>
/// <remarks>
/// The CUDA runtime is a separate asset from the llama.cpp build that needs it, which is why it
/// gets its own entry in the fetch list rather than being folded into the build's size. Somebody
/// choosing CUDA is agreeing to two downloads and should see two.
/// </remarks>
public sealed record EngineAsset(
    string Label,
    string FileName,
    string Url,
    string Sha256,
    long Bytes,
    string VendorFolder)
{
    /// <summary>The size as the interface states it.</summary>
    public string SizeText => Bytes >= 1_048_576L
        ? $"{(Bytes + 524_288L) / 1_048_576L} MB"
        : $"{(Bytes + 512L) / 1024L} KB";
}
