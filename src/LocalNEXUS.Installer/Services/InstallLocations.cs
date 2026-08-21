using System.IO;

namespace LocalNEXUS.Installer.Services;

/// <summary>
/// Every path the installer writes to.
/// </summary>
/// <remarks>
/// Per user, under Programs, the way VS Code and Discord install. Three reasons and all of them
/// matter.
///
/// There is no elevation prompt at all, which is a better first impression for an unsigned
/// installer than the alternative.
///
/// The application can repair itself. Program Files is not writable by a standard user, so an
/// application installed there could never re-download a broken engine binary without asking for
/// elevation every time it tried.
///
/// And it matches what the application already does, since the Python runtime, the config, the
/// saved graphs and the logs are all under the same user data folder already.
///
/// The engines go beside that user data rather than beside the executable, which is the one
/// choice here worth arguing about. Beside the executable would make uninstall a single directory
/// delete. Under the data folder means half a gigabyte of engine survives an application update
/// that replaces the install directory, and re-downloading that because the application moved a
/// version is not a thing to do to somebody. The application already searches this location: it
/// is the last candidate in AppPaths, after the executable's own folder and every parent of it.
/// </remarks>
public static class InstallLocations
{
    /// <summary>The name the application knows itself by on disk. Not the brand, the folder.</summary>
    public const string AppFolderName = "LocalNEXUS";

    /// <summary>The executable the payload is written as.</summary>
    public const string AppExeName = "LocalNEXUS.exe";

    /// <summary>Where the application is installed.</summary>
    public static string InstallRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        AppFolderName);

    /// <summary>The installed application executable.</summary>
    public static string AppExecutable => Path.Combine(InstallRoot, AppExeName);

    /// <summary>Where the uninstaller copy of this installer lives.</summary>
    public static string UninstallerPath => Path.Combine(InstallRoot, "LocalNEXUS-Setup.exe");

    /// <summary>The application's user data folder, which the installer never deletes wholesale.</summary>
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppFolderName);

    /// <summary>Where the engines are unpacked. Searched by the application as its last candidate.</summary>
    public static string VendorRoot => Path.Combine(DataRoot, "vendor");

    /// <summary>The folder one engine unpacks into.</summary>
    public static string VendorFolder(string name) => Path.Combine(VendorRoot, name);

    /// <summary>Where downloads are staged before being unpacked.</summary>
    public static string StagingRoot { get; } = Path.Combine(Path.GetTempPath(), "LocalNEXUS-setup");

    /// <summary>The desktop shortcut, when one is asked for.</summary>
    public static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "LocalNEXUS.lnk");

    /// <summary>The start menu shortcut, which is always written.</summary>
    public static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        "LocalNEXUS.lnk");

    /// <summary>True when a previous install is present, which is what turns this into a modify.</summary>
    public static bool IsInstalled => File.Exists(AppExecutable);

    /// <summary>Which engines are already on disk, so a modify does not fetch them again.</summary>
    public static bool HasLlama => File.Exists(Path.Combine(VendorFolder("llama"), "llama-server.exe"));

    /// <summary>True when Mesh LLM is present in either of the two shapes its archive unpacks into.</summary>
    public static bool HasMesh
        => File.Exists(Path.Combine(VendorFolder("mesh"), "mesh-bundle", "mesh-llm.exe"))
           || File.Exists(Path.Combine(VendorFolder("mesh"), "mesh-llm.exe"));

    /// <summary>True when uv is present.</summary>
    public static bool HasUv => File.Exists(Path.Combine(VendorFolder("uv"), "uv.exe"));
}
