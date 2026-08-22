using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// Tracks the project that output nodes write into, resolves paths inside it, and says what sort
/// of project it is.
/// </summary>
/// <remarks>
/// Resolution is the security boundary of the slice. Every path an output node produces is
/// checked to be inside the opened project, so a subfolder or file name containing traversal
/// segments cannot reach the rest of the disk. None of that is about Unity, and neither was any of
/// the rest of this; only the name was.
///
/// What is about Unity is <see cref="Kind"/>, which is read once when a folder is opened and is
/// what puts the Unity write rules in force or leaves them out. It is detected rather than asked.
/// </remarks>
public sealed partial class ProjectService : ObservableObject
{
    /// <summary>Absolute path of the opened project folder, or null when nothing is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyPropertyChangedFor(nameof(ProjectName))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _projectPath;

    /// <summary>What sort of project is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnity))]
    [NotifyPropertyChangedFor(nameof(KindText))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private ProjectKind _kind = ProjectKind.None;

    /// <summary>True when the Unity write rules are in force.</summary>
    public bool IsUnity => Kind == ProjectKind.Unity;

    /// <summary>True when a folder is open.</summary>
    public bool HasProject => !string.IsNullOrWhiteSpace(ProjectPath);

    /// <summary>Leaf folder name of the opened project.</summary>
    public string? ProjectName => HasProject ? new DirectoryInfo(ProjectPath!).Name : null;

    /// <summary>What was detected, in two words, for anywhere the project is named.</summary>
    public string KindText => Kind switch
    {
        ProjectKind.Unity => "Unity project",
        ProjectKind.Plain => "C# project",
        _ => "No project"
    };

    /// <summary>One line description of the current project, shown in the title bar area.</summary>
    public string StatusText
    {
        get
        {
            if (!HasProject)
            {
                return "No project open";
            }

            return Kind == ProjectKind.Unity
                ? $"{ProjectName}  ({ProjectPath})  Unity project, so the Unity write rules are in force"
                : $"{ProjectName}  ({ProjectPath})  C# project, so the Unity write rules do not apply";
        }
    }

    /// <summary>
    /// Whether a folder is a Unity project.
    /// </summary>
    /// <remarks>
    /// Two signals rather than one, and neither of them is an <c>Assets</c> folder on its own.
    /// <c>ProjectVersion.txt</c> is written by the editor and by nothing else, which makes it the
    /// one worth leading with. An <c>Assets</c> folder is a common name outside Unity, so it counts
    /// only alongside a second folder that is not: <c>ProjectSettings</c>, or the package manifest.
    ///
    /// Static and side effect free so that anything needing the answer about a path it was handed,
    /// rather than about the open project, can ask without going through the open one.
    ///
    /// An override in the project's own settings wins over both signals, because detection is a
    /// guess made from folder names and somebody who has told the application what their project
    /// is has more information than the guess does.
    /// </remarks>
    public static ProjectKind Detect(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return ProjectKind.None;
        }

        // What somebody said, before what the folders suggest. Detection is right most of the
        // time and the answer decides which write rules apply, so being able to correct it
        // matters more than being able to argue with it: a project told it is not Unity when it
        // is loses the refusals that stop a scene quietly losing its scripts.
        if (ProjectSettings.Load(folder).KindOverride is { } overridden)
        {
            return overridden;
        }

        if (File.Exists(Path.Combine(folder, "ProjectSettings", "ProjectVersion.txt")))
        {
            return ProjectKind.Unity;
        }

        if (!Directory.Exists(Path.Combine(folder, "Assets")))
        {
            return ProjectKind.Plain;
        }

        return Directory.Exists(Path.Combine(folder, "ProjectSettings"))
               || File.Exists(Path.Combine(folder, "Packages", "manifest.json"))
            ? ProjectKind.Unity
            : ProjectKind.Plain;
    }

    /// <summary>
    /// Opens a folder as the active project, and works out what sort of project it is.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public void Open(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException($"The folder does not exist: {folder}");
        }

        var full = Path.GetFullPath(folder);
        ProjectPath = full;
        Kind = Detect(full);
    }

    /// <summary>Closes the current project.</summary>
    public void Close()
    {
        ProjectPath = null;
        Kind = ProjectKind.None;
    }

    /// <summary>
    /// Turns a subfolder and file name into an absolute path inside the opened project.
    /// </summary>
    /// <exception cref="InvalidOperationException">No project is open.</exception>
    /// <exception cref="ArgumentException">The file name is blank or the result escapes the project folder.</exception>
    public string ResolveTargetPath(string subfolder, string fileName)
    {
        if (!HasProject)
        {
            throw new InvalidOperationException("Open a project before running a graph that writes files.");
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("The output node has no file name set.", nameof(fileName));
        }

        var root = Path.GetFullPath(ProjectPath!);
        var relative = Path.Combine(subfolder ?? string.Empty, fileName);
        var candidate = Path.GetFullPath(Path.Combine(root, relative));

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The resolved path leaves the project folder: {candidate}", nameof(subfolder));
        }

        return candidate;
    }

    /// <summary>Formats a path relative to the project root for display in the feed.</summary>
    public string ToDisplayPath(string absolutePath)
    {
        if (!HasProject)
        {
            return absolutePath;
        }

        var relative = Path.GetRelativePath(ProjectPath!, absolutePath);
        return relative.StartsWith("..", StringComparison.Ordinal) ? absolutePath : relative.Replace('\\', '/');
    }
}
