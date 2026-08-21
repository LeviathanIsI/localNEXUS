namespace LocalNEXUS.App.Models.Extensions;

/// <summary>
/// What kind of thing a prerequisite is, which decides whether this application can do anything
/// about it.
/// </summary>
public enum PrerequisiteKind
{
    /// <summary>A command that has to be on the path. Installable.</summary>
    Executable,

    /// <summary>A package that has to be in the opened Unity project. Readable, not installable.</summary>
    UnityPackage,

    /// <summary>The Unity editor has to be running on the project. Not installable, not even knowable without trying.</summary>
    UnityEditor
}

/// <summary>
/// Something that has to be true before an extension can work.
/// </summary>
/// <param name="Kind">What sort of thing this is.</param>
/// <param name="Name">The command, the package id, or the thing that has to be running.</param>
/// <param name="Reason">What the extension needs it for, in a sentence a person can act on.</param>
/// <param name="InstallCommand">How to install it, when this application can. Null when it cannot.</param>
/// <param name="InstallArguments">Arguments for <paramref name="InstallCommand"/>.</param>
/// <param name="MinimumVersion">Lowest acceptable version, when that matters.</param>
/// <remarks>
/// The split that matters is between what can be installed and what can only be reported. Node
/// can be installed. Unity being open cannot, and offering to install it would be a button that
/// lies. So the two are different kinds rather than one kind with a flag, and the panel can only
/// offer to fix the ones it can actually fix.
/// </remarks>
public sealed record ExtensionPrerequisite(
    PrerequisiteKind Kind,
    string Name,
    string Reason,
    string? InstallCommand = null,
    IReadOnlyList<string>? InstallArguments = null,
    string? MinimumVersion = null)
{
    /// <summary>True when this application can install it rather than only telling somebody about it.</summary>
    public bool CanInstall => InstallCommand is not null;
}

/// <summary>
/// The answer to whether one prerequisite is met.
/// </summary>
/// <param name="Prerequisite">What was checked.</param>
/// <param name="Met">Whether it is satisfied.</param>
/// <param name="Detail">What was found, such as a version, or why the check failed.</param>
public sealed record PrerequisiteResult(ExtensionPrerequisite Prerequisite, bool Met, string Detail);
