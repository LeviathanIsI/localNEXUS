using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using LocalNEXUS.Installer.Models;

namespace LocalNEXUS.Installer.Services;

/// <summary>
/// Fetches one asset, verifies it, and says clearly when it could not.
/// </summary>
/// <remarks>
/// The hash is computed while the bytes are being written rather than by reading the file back
/// afterwards, which halves the disk traffic on a 400 MB download and means a corrupt file is
/// never left looking complete.
///
/// Every failure worth telling apart is told apart. No internet, a release that has moved, a
/// disk with no room and a file that arrived damaged are four different problems with four
/// different things to do about them, and an installer that reports all four as "download
/// failed" has told the person nothing.
/// </remarks>
public sealed class AssetDownloader : IDisposable
{
    private const int BufferSize = 128 * 1024;

    private readonly HttpClient _http;

    public AssetDownloader()
    {
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All
        })
        {
            // A slow connection on a 400 MB file must not be mistaken for a hang. Progress is
            // what tells the person it is alive, so the overall request is not given a deadline.
            Timeout = Timeout.InfiniteTimeSpan
        };

        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LocalNEXUS-Setup");
    }

    /// <summary>
    /// Downloads an asset to the staging folder and verifies its hash.
    /// </summary>
    /// <param name="asset">What to fetch.</param>
    /// <param name="onProgress">Fraction from zero to one, and the bytes so far.</param>
    /// <param name="ct">Cancels the download.</param>
    /// <returns>The path it was written to.</returns>
    /// <exception cref="SetupException">Named, with what to do about it.</exception>
    public async Task<string> DownloadAsync(
        EngineAsset asset,
        Action<double, long> onProgress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(InstallLocations.StagingRoot);
        var target = Path.Combine(InstallLocations.StagingRoot, asset.FileName);

        EnsureRoom(InstallLocations.StagingRoot, asset.Bytes, asset.Label);

        HttpResponseMessage response;

        try
        {
            response = await _http
                .GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SetupException(
                $"Could not reach the download for {asset.Label}. " +
                "Check that this machine is online and that a proxy or firewall is not blocking github.com. " +
                $"The address was {asset.Url}",
                ex)
            {
                CanRetry = true
            };
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new SetupException(
                    $"{asset.Label} is no longer at the address this installer was built with, which usually means " +
                    "this installer is an old one and the release has moved. Take a newer installer, or install " +
                    "LocalNEXUS on its own and place the binaries by hand. Each folder under the vendor directory " +
                    $"has a README naming what to download. The address was {asset.Url}");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new SetupException(
                    $"The server answered {(int)response.StatusCode} {response.ReasonPhrase} for {asset.Label}.")
                {
                    CanRetry = true
                };
            }

            var total = response.Content.Headers.ContentLength ?? asset.Bytes;

            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var destination = new FileStream(
                    target, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

                using var hash = SHA256.Create();

                var buffer = new byte[BufferSize];
                long written = 0;

                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false);

                    if (read == 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    hash.TransformBlock(buffer, 0, read, null, 0);

                    written += read;
                    onProgress(total > 0 ? Math.Clamp((double)written / total, 0d, 1d) : 0d, written);
                }

                hash.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                var actual = Convert.ToHexString(hash.Hash ?? Array.Empty<byte>()).ToLowerInvariant();

                if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    // Deleted rather than left in place, so a retry cannot pick up the bad copy
                    // and so nothing downstream ever unpacks it.
                    destination.Close();
                    TryDelete(target);

                    throw new SetupException(
                        $"{asset.Label} downloaded but did not match its checksum, which means it arrived damaged or " +
                        "was altered in transit. Nothing was installed from it. Trying again is usually enough.")
                    {
                        CanRetry = true
                    };
                }
            }
            catch (IOException ex) when (IsDiskFull(ex))
            {
                TryDelete(target);

                throw new SetupException(
                    $"The disk filled up while downloading {asset.Label}. Free some space and try again. " +
                    $"It needs {asset.SizeText} to download and about twice that once unpacked.",
                    ex)
                {
                    CanRetry = true
                };
            }
            catch (HttpRequestException ex)
            {
                TryDelete(target);

                throw new SetupException(
                    $"The connection dropped while downloading {asset.Label}. Trying again resumes from the start.",
                    ex)
                {
                    CanRetry = true
                };
            }
        }

        return target;
    }

    public void Dispose() => _http.Dispose();

    /// <summary>Checks there is room before starting, rather than finding out at ninety percent.</summary>
    private static void EnsureRoom(string path, long needed, string label)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));

            if (string.IsNullOrEmpty(root))
            {
                return;
            }

            var drive = new DriveInfo(root);

            // Twice the archive, because it is unpacked as well as downloaded.
            var required = needed * 2;

            if (drive.AvailableFreeSpace >= required)
            {
                return;
            }

            throw new SetupException(
                $"There is not enough room on {root} for {label}. " +
                $"It needs about {(required + 524_288L) / 1_048_576L} MB and there is " +
                $"{(drive.AvailableFreeSpace + 524_288L) / 1_048_576L} MB free. " +
                "Free some space and try again, or go back and untick a component.")
            {
                CanRetry = true
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // A drive that will not answer is not a reason to refuse to start. The write itself
            // reports the real problem if there is one.
        }
    }

    private static bool IsDiskFull(IOException ex)
    {
        const int DiskFull = unchecked((int)0x80070070);
        const int HandleDiskFull = unchecked((int)0x80070027);

        return ex.HResult == DiskFull || ex.HResult == HandleDiskFull;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Staging is a temp folder. A file left behind there is not worth a second failure.
        }
    }
}
