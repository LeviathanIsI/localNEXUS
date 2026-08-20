namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>What kind of member a signature describes.</summary>
public enum IndexedMemberKind
{
    /// <summary>A method or constructor.</summary>
    Method,

    /// <summary>A property or indexer.</summary>
    Property,

    /// <summary>A field.</summary>
    Field,

    /// <summary>An event.</summary>
    Event
}

/// <summary>
/// One member of a type, kept as its signature rather than its body.
/// </summary>
/// <remarks>
/// Bodies are deliberately dropped. What a request needs to know about existing code is what it
/// can call and what it can set, and a signature answers that in a fraction of the space, which
/// is the whole point when the context window belongs to a local model.
/// </remarks>
/// <param name="Kind">Method, property, field or event.</param>
/// <param name="Name">The member name on its own, for matching.</param>
/// <param name="Signature">The declaration as it would be written, without a body.</param>
/// <param name="IsSerialized">
/// True when Unity will serialise this field, meaning a public field or one marked
/// <c>[SerializeField]</c>. Renaming one of these loses data unless it carries
/// <c>[FormerlySerializedAs]</c>, which is why the index records it.
/// </param>
public sealed record IndexedMember(
    IndexedMemberKind Kind,
    string Name,
    string Signature,
    bool IsSerialized);
