namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// What sort of codebase is open, which decides whether the Unity rules are in force.
/// </summary>
/// <remarks>
/// A mode rather than a category of user. Everything this application does works on any C# project;
/// what a Unity project adds is a set of edits that compile cleanly and destroy data, and therefore
/// a set of refusals that would be nonsense anywhere else.
///
/// Detected rather than asked, because somebody who has just opened their own project already knows
/// what it is and being asked is being made to do the application's work.
/// </remarks>
public enum ProjectKind
{
    /// <summary>Nothing is open, so there is nothing to be.</summary>
    None,

    /// <summary>An ordinary C# codebase. The Unity rules do not apply and do not run.</summary>
    Plain,

    /// <summary>A Unity project. Every Unity rule is in force.</summary>
    Unity
}
