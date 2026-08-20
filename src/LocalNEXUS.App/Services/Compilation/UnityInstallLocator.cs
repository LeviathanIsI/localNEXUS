using System.Globalization;
using System.IO;
using System.Text.Json;

namespace LocalNEXUS.App.Services.Compilation;

/// <summary>
/// Finds the Unity editor a project was made with.
/// </summary>
/// <remarks>
/// The version the project asks for is preferred, because the API surface changes between them
/// and checking code against the wrong one produces errors that are not real. When that exact
/// version is not installed the newest one is used and the caller is told, which is a weaker
/// answer than the right editor but a much better one than refusing to check at all.
/// </remarks>
public static class UnityInstallLocator
{
    /// <summary>The file inside a Unity project that records which editor made it.</summary>
    private const string VersionFileName = "ProjectVersion.txt";

    /// <summary>Where the Hub records an install folder other than the default one.</summary>
    private static readonly string SecondaryInstallPathFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "UnityHub",
        "secondaryInstallPath.json");

    /// <summary>An installed editor: its version, and the Data folder its assemblies live in.</summary>
    /// <param name="Version">The editor version, for example <c>6000.5.5f1</c>.</param>
    /// <param name="DataPath">The editor's <c>Editor\Data</c> folder.</param>
    public readonly record struct UnityInstall(string Version, string DataPath);

    /// <summary>
    /// Reads the editor version a project was last opened with, or null when the project does
    /// not record one.
    /// </summary>
    public static string? ReadProjectVersion(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        var file = Path.Combine(projectPath, "ProjectSettings", VersionFileName);

        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            foreach (var line in File.ReadLines(file))
            {
                const string key = "m_EditorVersion:";

                if (line.StartsWith(key, StringComparison.Ordinal))
                {
                    return line[key.Length..].Trim();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A project whose version file cannot be read still gets the newest editor.
        }

        return null;
    }

    /// <summary>Every editor installed on this machine, oldest first.</summary>
    public static IReadOnlyList<UnityInstall> EnumerateInstalls()
    {
        var found = new List<UnityInstall>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateEditorRoots())
        {
            foreach (var versionFolder in SafeGetDirectories(root))
            {
                var data = Path.Combine(versionFolder, "Editor", "Data");

                if (Directory.Exists(data) && seen.Add(data))
                {
                    found.Add(new UnityInstall(new DirectoryInfo(versionFolder).Name, data));
                }
            }
        }

        // A standalone install has no version folder between the root and the editor, so its
        // version is not in the path and is not something worth guessing at.
        foreach (var standalone in EnumerateStandaloneDataFolders())
        {
            if (Directory.Exists(standalone) && seen.Add(standalone))
            {
                found.Add(new UnityInstall("unknown", standalone));
            }
        }

        found.Sort((a, b) => CompareVersions(a.Version, b.Version));
        return found;
    }

    /// <summary>
    /// Picks the editor to compile against for a project. Returns null when nothing is installed.
    /// </summary>
    /// <param name="projectPath">The Unity project, used to read its recorded editor version.</param>
    /// <param name="exactVersion">True when the returned install is the version the project asks for.</param>
    public static UnityInstall? Resolve(string? projectPath, out bool exactVersion)
    {
        exactVersion = false;

        var installs = EnumerateInstalls();
        if (installs.Count == 0)
        {
            return null;
        }

        if (ReadProjectVersion(projectPath) is { } wanted)
        {
            foreach (var install in installs)
            {
                if (string.Equals(install.Version, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    exactVersion = true;
                    return install;
                }
            }
        }

        return installs[^1];
    }

    /// <summary>
    /// Orders Unity versions by their numeric parts rather than as text, so that 6000.10 is
    /// newer than 6000.9 rather than older.
    /// </summary>
    private static int CompareVersions(string left, string right)
    {
        var a = NumericParts(left);
        var b = NumericParts(right);

        for (var i = 0; i < Math.Max(a.Count, b.Count); i++)
        {
            var x = i < a.Count ? a[i] : 0;
            var y = i < b.Count ? b[i] : 0;

            if (x != y)
            {
                return x.CompareTo(y);
            }
        }

        return string.CompareOrdinal(left, right);
    }

    /// <summary>
    /// The leading numbers of a version string. A Unity version ends in a release suffix such as
    /// <c>5f1</c>, so digits are read up to the first letter of each segment.
    /// </summary>
    private static List<int> NumericParts(string version)
    {
        var parts = new List<int>();

        foreach (var segment in version.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = new string(segment.TakeWhile(char.IsDigit).ToArray());

            parts.Add(int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0);
        }

        return parts;
    }

    private static IEnumerable<string> EnumerateEditorRoots()
    {
        foreach (var programFiles in ProgramFilesFolders())
        {
            yield return Path.Combine(programFiles, "Unity", "Hub", "Editor");
        }

        if (ReadSecondaryInstallPath() is { } secondary)
        {
            yield return secondary;
            yield return Path.Combine(secondary, "Editor");
        }
    }

    private static IEnumerable<string> EnumerateStandaloneDataFolders()
    {
        foreach (var programFiles in ProgramFilesFolders())
        {
            yield return Path.Combine(programFiles, "Unity", "Editor", "Data");
        }
    }

    private static IEnumerable<string> ProgramFilesFolders()
    {
        foreach (var folder in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 })
        {
            if (!string.IsNullOrEmpty(folder))
            {
                yield return folder;
            }
        }
    }

    private static string[] SafeGetDirectories(string root)
    {
        try
        {
            return Directory.Exists(root) ? Directory.GetDirectories(root) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Reads the Hub's record of an install folder outside Program Files. The file holds a bare
    /// JSON string rather than an object.
    /// </summary>
    private static string? ReadSecondaryInstallPath()
    {
        try
        {
            if (!File.Exists(SecondaryInstallPathFile))
            {
                return null;
            }

            var json = File.ReadAllText(SecondaryInstallPathFile).Trim();

            if (json.Length == 0)
            {
                return null;
            }

            var path = JsonSerializer.Deserialize<string>(json);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
