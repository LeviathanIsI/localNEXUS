namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// Which rule refused a write.
/// </summary>
/// <remarks>
/// Every one of these describes a change that compiles cleanly and silently breaks something, and
/// each is a different mistake with a different fix. They all used to arrive as the same exception
/// carrying only a sentence, so anything downstream could tell that a write had been refused and
/// not which of seven quite different things had happened. Reading it back out of the sentence
/// would be coupling to prose.
/// </remarks>
public enum ProjectWriteRule
{
    /// <summary>A MonoBehaviour only binds when its file name matches its class name exactly.</summary>
    FileNameMustMatchBehaviour,

    /// <summary>A type that vanishes from a file takes its scene bindings with it.</summary>
    TypeMayNotDisappear,

    /// <summary>Changing a namespace changes the identity Unity stored.</summary>
    NamespaceMayNotChange,

    /// <summary>A serialized field renamed without a shim silently loses its value in every scene.</summary>
    SerializedFieldMayNotBeRenamed,

    /// <summary>A type that stops deriving from MonoBehaviour stops being a component.</summary>
    BehaviourMustStayBehaviour,

    /// <summary>An edit was planned and there is no such file.</summary>
    FileMustExistToEdit,

    /// <summary>A new file was planned and one is already there.</summary>
    FileMustNotExistToCreate
}

/// <summary>
/// A write that would break something Unity binds through, refused before it reached disk.
/// </summary>
public sealed class UnityScriptRuleException : Exception
{
    public UnityScriptRuleException(ProjectWriteRule rule, string message)
        : base(message)
    {
        Rule = rule;
    }

    /// <summary>Which rule refused it.</summary>
    public ProjectWriteRule Rule { get; }
}
