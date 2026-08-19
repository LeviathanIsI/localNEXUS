using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// Tracks the Unity project that output nodes write into, and resolves paths inside it.
/// </summary>
/// <remarks>
/// Resolution is the security boundary of the slice. Every path an output node produces is
/// checked to be inside the opened project, so a subfolder or file name containing traversal
/// segments cannot reach the rest of the disk.
/// </remarks>
public sealed partial class UnityProjectService : ObservableObject
{
    /// <summary>Absolute path of the opened project folder, or null when nothing is open.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProject))]
    [NotifyPropertyChangedFor(nameof(ProjectName))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _projectPath;

    /// <summary>True when the opened folder actually looks like a Unity project.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _looksLikeUnityProject;

    /// <summary>True when a folder is open.</summary>
    public bool HasProject => !string.IsNullOrWhiteSpace(ProjectPath);

    /// <summary>Leaf folder name of the opened project.</summary>
    public string? ProjectName => HasProject ? new DirectoryInfo(ProjectPath!).Name : null;

    /// <summary>One line description of the current project, shown in the title bar area.</summary>
    public string StatusText
    {
        get
        {
            if (!HasProject)
            {
                return "No project open";
            }

            return LooksLikeUnityProject
                ? $"{ProjectName}  ({ProjectPath})"
                : $"{ProjectName}  ({ProjectPath})  no Assets folder found";
        }
    }

    /// <summary>
    /// Opens a folder as the active project. Folders without an <c>Assets</c> directory are
    /// still accepted so that a new project can be set up, but they are flagged.
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
        LooksLikeUnityProject = Directory.Exists(Path.Combine(full, "Assets"));
    }

    /// <summary>Closes the current project.</summary>
    public void Close()
    {
        ProjectPath = null;
        LooksLikeUnityProject = false;
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
            throw new InvalidOperationException("Open a Unity project before running a graph that writes files.");
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
