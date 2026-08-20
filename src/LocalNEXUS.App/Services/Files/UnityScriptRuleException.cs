namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// Thrown when a write would break how Unity binds scripts to the things that use them.
/// </summary>
/// <remarks>
/// A refusal rather than a warning, deliberately. Every rule this carries describes a change that
/// compiles perfectly and silently breaks a scene: a missing script on a prefab, a serialized
/// value gone, a component that no longer binds. A warning in a feed nobody rereads is not a
/// defence against that, because the damage is invisible until someone opens the scene.
/// </remarks>
public sealed class UnityScriptRuleException : Exception
{
    public UnityScriptRuleException(string message)
        : base(message)
    {
    }
}
