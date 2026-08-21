using System.IO;
using System.IO.Compression;
using System.Reflection;
using LocalNEXUS.Installer.Models;

namespace LocalNEXUS.Installer.Services;

/// <summary>What the installer is doing right now, for the progress line.</summary>
/// <param name="Label">The file or step being worked on.</param>
/// <param name="Fraction">How far through, zero to one.</param>
public sealed record SetupProgress(string Label, double Fraction);

/// <summary>
/// Does the install: writes the application, fetches the engines, unpacks them, and records it.
/// </summary>
/// <remarks>
/// The application itself is written from a resource compiled into this executable rather than
/// downloaded, which is why it does not appear in the fetch list. Only the engines are fetched,
/// and only because they are somebody else's software under somebody else's licence.
/// </remarks>
public sealed class SetupRunner
{
    private const string PayloadResource = "LocalNEXUS.Payload.exe";

    private readonly AssetDownloader _downloader;
    private readonly Action<string> _log;
    private readonly Action<SetupProgress> _progress;

    public SetupRunner(AssetDownloader downloader, Action<string> log, Action<SetupProgress> progress)
    {
        _downloader = downloader;
        _log = log;
        _progress = progress;
    }

    /// <summary>True when this build carries the application payload.</summary>
    public static bool HasPayload
        => Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains(PayloadResource);

    /// <summary>
    /// Runs the whole install.
    /// </summary>
    /// <exception cref="SetupException">Something failed, said plainly.</exception>
    public async Task RunAsync(
        IReadOnlyList<EngineAsset> assets,
        bool desktopShortcut,
        string version,
        CancellationToken ct)
    {
        // The application first, so a run that fails part way through the engines still leaves
        // something that starts and can be pointed at binaries by hand.
        _progress(new SetupProgress("LocalNEXUS", 0d));
        WriteApplication();

        var total = assets.Count + 1;
        var done = 1;

        foreach (var asset in assets)
        {
            ct.ThrowIfCancellationRequested();

            _log($"Downloading {asset.Label}, {asset.SizeText}");

            var completed = done;

            var archive = await _downloader
                .DownloadAsync(
                    asset,
                    (fraction, bytes) => _progress(new SetupProgress(
                        asset.Label,
                        (completed + fraction) / total)),
                    ct)
                .ConfigureAwait(false);

            _log($"Verified {asset.Label}");

            var destination = InstallLocations.VendorFolder(asset.VendorFolder);
            _log($"Unpacking into {destination}");

            Extract(archive, destination, asset.Label);
            TryDelete(archive);

            done++;
            _progress(new SetupProgress(asset.Label, (double)done / total));
        }

        _log("Writing shortcuts");
        ShortcutWriter.Write(InstallLocations.StartMenuShortcut, InstallLocations.AppExecutable, "LocalNEXUS");

        if (desktopShortcut)
        {
            ShortcutWriter.Write(InstallLocations.DesktopShortcut, InstallLocations.AppExecutable, "LocalNEXUS");
        }
        else
        {
            ShortcutWriter.Remove(InstallLocations.DesktopShortcut);
        }

        _log("Recording the install");
        var footprint = assets.Sum(a => a.Bytes * 2) + PayloadSize();
        UninstallRegistrar.Register(version, footprint);

        _progress(new SetupProgress("Done", 1d));
        _log("Finished.");
    }

    /// <summary>
    /// Removes what was installed.
    /// </summary>
    /// <param name="removeUserData">
    /// When false, which is the default, the settings, saved graphs, models catalogue and Python
    /// runtime are left alone. Only the engines this installer put there are taken.
    /// </param>
    public static void Uninstall(bool removeUserData, Action<string> log)
    {
        log("Removing shortcuts");
        ShortcutWriter.Remove(InstallLocations.StartMenuShortcut);
        ShortcutWriter.Remove(InstallLocations.DesktopShortcut);

        log("Removing the engines");

        foreach (var folder in new[] { "llama", "mesh", "uv", "python" })
        {
            TryDeleteFolder(InstallLocations.VendorFolder(folder));
        }

        TryDeleteFolder(InstallLocations.VendorRoot, onlyIfEmpty: true);

        if (removeUserData)
        {
            // Asked for explicitly, and the checkbox says what goes with it.
            log("Removing settings, saved graphs and the Python runtime");
            TryDeleteFolder(InstallLocations.DataRoot);
        }
        else
        {
            log($"Leaving your settings and saved graphs in {InstallLocations.DataRoot}");
        }

        log("Removing the application");

        // Everything except the running uninstaller, which cannot delete itself while it is the
        // thing executing. It is handed to the shell to remove once this process ends.
        foreach (var file in SafeFiles(InstallLocations.InstallRoot))
        {
            if (string.Equals(file, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteFile(file);
        }

        UninstallRegistrar.Unregister();
        log("Finished.");

        ScheduleSelfDelete();
    }

    private static void WriteApplication()
    {
        if (!HasPayload)
        {
            throw new SetupException(
                "This installer was built without the application inside it, so there is nothing to install. " +
                "That is a packaging fault rather than anything you did. Take a release build from the project's " +
                "releases page.");
        }

        Directory.CreateDirectory(InstallLocations.InstallRoot);

        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
            ?? throw new SetupException("The application payload could not be read out of this installer.");

        try
        {
            using (var destination = new FileStream(
                InstallLocations.AppExecutable, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(destination);
            }

            // A copy of this installer goes beside the application, which is what Add or remove
            // programs runs to uninstall and what a person runs again to modify.
            if (Environment.ProcessPath is { } self
                && !string.Equals(self, InstallLocations.UninstallerPath, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(self, InstallLocations.UninstallerPath, overwrite: true);
            }
        }
        catch (IOException ex) when (IsInUse(ex))
        {
            throw new SetupException(
                "LocalNEXUS is running, so it could not be replaced. Close it and try again.",
                ex)
            {
                CanRetry = true
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SetupException(
                $"The application could not be written to {InstallLocations.InstallRoot}: {ex.Message}",
                ex)
            {
                CanRetry = true
            };
        }
    }

    private static long PayloadSize()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource);
            return stream?.Length ?? 0L;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            return 0L;
        }
    }

    private static void Extract(string archive, string destination, string label)
    {
        try
        {
            Directory.CreateDirectory(destination);

            // Overwriting rather than failing on an existing entry, so a repair over a partial
            // install completes instead of stopping on the first file it already has.
            using var zip = ZipFile.OpenRead(archive);

            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));

                // An archive entry naming a path outside the destination is the zip slip
                // problem, and these archives come off the internet.
                if (!target.StartsWith(Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
                {
                    throw new SetupException(
                        $"{label} contains an entry that would be written outside its own folder, so it was " +
                        "refused. Nothing from it has been installed.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entry.ExtractToFile(target, overwrite: true);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new SetupException(
                $"{label} passed its checksum but could not be opened as an archive, which should not happen. " +
                "Trying again is worth one attempt.",
                ex)
            {
                CanRetry = true
            };
        }
        catch (IOException ex) when (IsDiskFull(ex))
        {
            throw new SetupException(
                $"The disk filled up while unpacking {label}. Free some space and try again.",
                ex)
            {
                CanRetry = true
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SetupException($"{label} could not be unpacked: {ex.Message}", ex) { CanRetry = true };
        }
    }

    private static IEnumerable<string> SafeFiles(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static void ScheduleSelfDelete()
    {
        if (Environment.ProcessPath is not { } self)
        {
            return;
        }

        try
        {
            // A short wait, then remove the uninstaller and the folder it was sitting in. Run
            // detached so it outlives this process, which is the only way a program can remove
            // the file it is running from.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 3 /nobreak >nul & del /f /q \"{self}\" & rmdir /s /q \"{InstallLocations.InstallRoot}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException)
        {
            // The install folder is left holding one executable. Untidy, not broken.
        }
    }

    private static void TryDeleteFolder(string folder, bool onlyIfEmpty = false)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            if (onlyIfEmpty && (Directory.GetFiles(folder).Length > 0 || Directory.GetDirectories(folder).Length > 0))
            {
                return;
            }

            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reported by what the caller logs next rather than stopping the uninstall.
        }
    }

    private static void TryDeleteFile(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Same reasoning.
        }
    }

    private static void TryDelete(string file) => TryDeleteFile(file);

    private static bool IsInUse(IOException ex)
    {
        const int SharingViolation = unchecked((int)0x80070020);
        const int LockViolation = unchecked((int)0x80070021);

        return ex.HResult == SharingViolation || ex.HResult == LockViolation;
    }

    private static bool IsDiskFull(IOException ex)
    {
        const int DiskFull = unchecked((int)0x80070070);
        const int HandleDiskFull = unchecked((int)0x80070027);

        return ex.HResult == DiskFull || ex.HResult == HandleDiskFull;
    }
}
