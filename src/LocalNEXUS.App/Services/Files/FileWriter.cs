using System.IO;
using System.Text;

namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// Writes generated files to disk.
/// </summary>
/// <remarks>
/// Files are written as UTF-8 without a byte order mark, which is what Unity expects for C#
/// source. Only creation and overwrite are supported in this slice.
/// </remarks>
public sealed class FileWriter
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="absolutePath"/>, creating any missing
    /// directories, and returns the number of bytes written.
    /// </summary>
    public async Task<long> WriteAsync(string absolutePath, string content, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        var directory = Path.GetDirectoryName(absolutePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var bytes = Utf8WithoutBom.GetBytes(content ?? string.Empty);

        await using var stream = new FileStream(
            absolutePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true);

        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);

        return bytes.LongLength;
    }
}
