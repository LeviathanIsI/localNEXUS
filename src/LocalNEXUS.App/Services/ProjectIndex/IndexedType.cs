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
        int line)
    {
        Name = name;
        Namespace = @namespace;
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

    /// <summary>Namespace and name, which is what Unity resolves a serialised reference by.</summary>
    public string FullName => Namespace.Length == 0 ? Name : $"{Namespace}.{Name}";

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
