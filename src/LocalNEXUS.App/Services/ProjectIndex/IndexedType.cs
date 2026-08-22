namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>One type declared somewhere in the project.</summary>
public sealed class IndexedType
{
    public IndexedType(
        string name,
        string @namespace,
        IndexedTypeKind kind,
        IReadOnlyList<string> baseTypes,
        IReadOnlyList<IndexedMember> members,
        bool isPartial,
        int line,
        string containingTypes = "")
    {
        Name = name;
        Namespace = @namespace;
        ContainingTypes = containingTypes;
        Kind = kind;
        BaseTypes = baseTypes;
        Members = members;
        IsPartial = isPartial;
        Line = line;
    }

    /// <summary>The type name on its own.</summary>
    public string Name { get; }

    /// <summary>Its namespace, or an empty string when it is in the global one.</summary>
    public string Namespace { get; }

    /// <summary>
    /// The types this one is declared inside, outermost first, or empty when it is top level.
    /// </summary>
    /// <remarks>
    /// Kept apart from the namespace because they are different things that only look alike once
    /// they are joined by dots. A type moving between namespaces breaks every scene referencing
    /// it, which is a rule this application enforces; a type moving between containing types is
    /// somebody restructuring a file.
    /// </remarks>
    public string ContainingTypes { get; }

    /// <summary>What kind of type it is.</summary>
    public IndexedTypeKind Kind { get; }

    /// <summary>The base type and interfaces exactly as written.</summary>
    public IReadOnlyList<string> BaseTypes { get; }

    /// <summary>Its public surface, as signatures.</summary>
    public IReadOnlyList<IndexedMember> Members { get; }

    /// <summary>True when the declaration is partial, so the type may be spread over several files.</summary>
    public bool IsPartial { get; }

    /// <summary>One based line the declaration starts on.</summary>
    public int Line { get; }

    /// <summary>
    /// Everything it is declared inside, then its name.
    /// </summary>
    /// <remarks>
    /// Nesting used to be left out, so a class called ItemStack inside a class called Inventory
    /// was recorded as Game.ItemStack, which is the name of a different type that may or may not
    /// exist. Three things trust this and all three were being answered wrongly: the duplicate
    /// guard asks whether the project already holds a name, the elicitation check asks whether a
    /// request names anything real, and the convergence meter counts a name only if the project
    /// has it.
    /// </remarks>
    public string FullName
    {
        get
        {
            var prefix = Namespace.Length == 0
                ? ContainingTypes
                : ContainingTypes.Length == 0
                    ? Namespace
                    : $"{Namespace}.{ContainingTypes}";

            return prefix.Length == 0 ? Name : $"{prefix}.{Name}";
        }
    }

    /// <summary>True when Unity attaches this to a GameObject, so its file name has to match it.</summary>
    public bool IsMonoBehaviour => Kind == IndexedTypeKind.MonoBehaviour;

    /// <summary>The fields Unity will serialise, which cannot be renamed without losing data.</summary>
    public IEnumerable<IndexedMember> SerializedFields
        => Members.Where(m => m.Kind == IndexedMemberKind.Field && m.IsSerialized);

    /// <summary>The declaration line as it would be written, for the compact digest.</summary>
    public string Declaration
    {
        get
        {
            var keyword = Kind switch
            {
                IndexedTypeKind.Interface => "interface",
                IndexedTypeKind.Enum => "enum",
                IndexedTypeKind.Struct => "struct",
                IndexedTypeKind.Record => "record",
                _ => "class"
            };

            var bases = BaseTypes.Count == 0 ? string.Empty : " : " + string.Join(", ", BaseTypes);
            return $"{keyword} {Name}{bases}";
        }
    }

    public override string ToString() => FullName;
}
