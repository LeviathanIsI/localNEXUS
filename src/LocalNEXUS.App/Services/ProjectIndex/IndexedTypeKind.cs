namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>
/// What kind of thing a declared type is, as far as syntax alone can tell.
/// </summary>
/// <remarks>
/// The two Unity flavours are here rather than in a separate flag because they are what a request
/// is usually about: a component to attach or an asset to configure. They are decided from the
/// base type list, which is syntax, so a type deriving from something that itself derives from
/// MonoBehaviour reads as a plain class. That is a known limit of not running a semantic model
/// over the whole project, and it is worth what it saves.
/// </remarks>
public enum IndexedTypeKind
{
    /// <summary>A plain class.</summary>
    Class,

    /// <summary>A value type.</summary>
    Struct,

    /// <summary>An interface.</summary>
    Interface,

    /// <summary>An enumeration.</summary>
    Enum,

    /// <summary>A record class or record struct.</summary>
    Record,

    /// <summary>A class deriving from MonoBehaviour, so it attaches to a GameObject.</summary>
    MonoBehaviour,

    /// <summary>A class deriving from ScriptableObject, so it exists as an asset.</summary>
    ScriptableObject
}
