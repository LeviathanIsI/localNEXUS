using System.IO;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// Every location the application reads from or writes to on disk.
/// </summary>
/// <remarks>
/// User data lives under <c>%LOCALAPPDATA%\LocalNEXUS</c> and never inside the repository, so a
/// clone stays clean and models are never at risk of being committed.
/// </remarks>
public static class AppPaths
{
    /// <summary>Name of the llama.cpp server executable that the app spawns for local models.</summary>
    public const string LlamaServerExecutableName = "llama-server.exe";

    /// <summary>
    /// Name of the Mesh LLM node executable, which is the process the distributed path runs on.
    /// </summary>
    public const string MeshExecutableName = "mesh-llm.exe";

    /// <summary>Root of the per user data folder.</summary>
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LocalNEXUS");

    /// <summary>Default folder scanned for GGUF model files.</summary>
    public static string Models { get; } = Path.Combine(Root, "models");

    /// <summary>Folder that saved graphs are written to.</summary>
    public static string Graphs { get; } = Path.Combine(Root, "graphs");

    /// <summary>Folder that application and llama-server logs are written to.</summary>
    public static string Logs { get; } = Path.Combine(Root, "logs");

    /// <summary>Path of the persisted application configuration.</summary>
    public static string ConfigFile { get; } = Path.Combine(Root, "config.json");

    /// <summary>Creates the data folders on first run. Safe to call repeatedly.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Models);
        Directory.CreateDirectory(Graphs);
        Directory.CreateDirectory(Logs);
    }

    /// <summary>Returns a timestamped log file path inside <see cref="Logs"/>.</summary>
    public static string CreateLogFilePath(string prefix)
    {
        Directory.CreateDirectory(Logs);
        var safePrefix = string.Concat(prefix.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Logs, $"{safePrefix}-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");
    }

    /// <summary>
    /// Locates the bundled llama-server executable.
    /// </summary>
    /// <remarks>
    /// The binaries are fetched by the user rather than committed, so they can sit either next
    /// to the built application or in the repository's <c>vendor\llama</c> folder while working
    /// from a development build. Both are searched, followed by the user data folder.
    /// </remarks>
    public static string? FindLlamaServerExecutable() => FindLlamaExecutable(LlamaServerExecutableName);

    /// <summary>
    /// Locates the bundled Mesh LLM executable. Its release bundle carries a native runtime
    /// tree beside the executable, so the whole bundle is placed under <c>vendor\mesh</c>
    /// rather than the executable alone.
    /// </summary>
    public static string? FindMeshExecutable()
    {
        foreach (var candidate in EnumerateMeshSearchDirectories())
        {
            var executable = Path.Combine(candidate, MeshExecutableName);
            if (File.Exists(executable))
            {
                return executable;
            }
        }

        return null;
    }

    /// <summary>Every directory searched for the Mesh LLM executable, in priority order.</summary>
    public static IEnumerable<string> EnumerateMeshSearchDirectories()
    {
        foreach (var directory in EnumerateVendorDirectories("mesh"))
        {
            yield return directory;

            // The published release bundle keeps the executable one level down beside its
            // native runtimes, so both shapes resolve without the user rearranging anything.
            yield return Path.Combine(directory, "mesh-bundle");
        }
    }

    private static string? FindLlamaExecutable(string executableName)
    {
        foreach (var candidate in EnumerateLlamaSearchDirectories())
        {
            var executable = Path.Combine(candidate, executableName);
            if (File.Exists(executable))
            {
                return executable;
            }
        }

        return null;
    }

    /// <summary>Every directory searched for the llama.cpp executables, in priority order.</summary>
    public static IEnumerable<string> EnumerateLlamaSearchDirectories() => EnumerateVendorDirectories("llama");

    /// <summary>
    /// Every place a bundled vendor folder may live, in priority order. Resolution has to give
    /// the same answer from a development run and from the published single file executable,
    /// which is why the process path is yielded alongside the base directory rather than one
    /// being assumed to equal the other.
    /// </summary>
    private static IEnumerable<string> EnumerateVendorDirectories(string vendorName)
    {
        var baseDirectory = AppContext.BaseDirectory;

        yield return Path.Combine(baseDirectory, "vendor", vendorName);

        if (Environment.ProcessPath is { } processPath
            && Path.GetDirectoryName(processPath) is { } processDirectory
            && !string.Equals(processDirectory, baseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(processDirectory, "vendor", vendorName);
        }

        // Walk up from the build output towards the repository root so that a development run
        // finds the vendor folder without a build step that copies the binaries around.
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            yield return Path.Combine(directory.FullName, "vendor", vendorName);
            directory = directory.Parent;
        }

        yield return Path.Combine(Root, "vendor", vendorName);
    }
}
