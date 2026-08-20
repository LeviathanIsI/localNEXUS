using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LocalNEXUS.App.Services.ProjectIndex;

/// <summary>
/// Reads one C# file and records what it declares, using the syntax tree and nothing else.
/// </summary>
/// <remarks>
/// Deliberately syntax only. Loading a Unity project through MSBuildWorkspace performs a design
/// time build per project, and Unity regenerates its csproj files on every recompile, so the
/// thing being loaded is a moving target that is expensive to load and known to fail on exactly
/// this shape of project. Parsing is lazy, thread safe and cheap to repeat, which is what an
/// index over a few thousand files needs.
///
/// The cost is that nothing here is resolved. A base type list holds the words that were written,
/// not the types they bind to, and a referenced name may be a namespace or a static class or a
/// variable. That is enough to say what exists and which files are near each other, which is what
/// the index is for. Where a question genuinely needs symbols, the compile checker already
/// assembles a reference set and a real compilation can be built from it.
/// </remarks>
public static class SourceFileParser
{
    /// <summary>The version Unity accepts for game code, so the parser agrees with the compiler.</summary>
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp9);

    /// <summary>Base type names that make a class a Unity component.</summary>
    private static readonly HashSet<string> MonoBehaviourBases = new(StringComparer.Ordinal)
    {
        "MonoBehaviour", "UnityEngine.MonoBehaviour", "NetworkBehaviour", "StateMachineBehaviour"
    };

    /// <summary>Base type names that make a class a Unity asset.</summary>
    private static readonly HashSet<string> ScriptableObjectBases = new(StringComparer.Ordinal)
    {
        "ScriptableObject", "UnityEngine.ScriptableObject"
    };

    /// <summary>
    /// Parses a file. Returns null when it cannot be read, because one unreadable file must not
    /// stop an index over several thousand of them.
    /// </summary>
    public static IndexedFile? Parse(string absolutePath, string relativePath, CancellationToken ct)
    {
        string text;
        FileInfo info;

        try
        {
            info = new FileInfo(absolutePath);
            text = File.ReadAllText(absolutePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var tree = CSharpSyntaxTree.ParseText(text, ParseOptions, path: relativePath, cancellationToken: ct);
        var root = tree.GetCompilationUnitRoot(ct);

        var declared = new List<IndexedType>();
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            ct.ThrowIfCancellationRequested();

            var type = Describe(declaration, tree);
            declared.Add(type);
            declaredNames.Add(type.Name);
        }

        var referenced = CollectReferencedNames(root, declaredNames);

        return new IndexedFile(
            relativePath,
            info.LastWriteTimeUtc,
            info.Length,
            FirstNamespace(root),
            declared,
            referenced);
    }

    private static IndexedType Describe(BaseTypeDeclarationSyntax declaration, SyntaxTree tree)
    {
        var baseTypes = declaration.BaseList?.Types
            .Select(t => t.Type.ToString())
            .ToList() ?? new List<string>();

        var kind = KindOf(declaration, baseTypes);
        var members = declaration is TypeDeclarationSyntax typeDeclaration
            ? DescribeMembers(typeDeclaration)
            : DescribeEnumMembers(declaration);

        var isPartial = declaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
        var line = tree.GetLineSpan(declaration.Identifier.Span).StartLinePosition.Line + 1;

        return new IndexedType(
            declaration.Identifier.ValueText,
            NamespaceOf(declaration),
            kind,
            baseTypes,
            members,
            isPartial,
            line);
    }

    private static IndexedTypeKind KindOf(BaseTypeDeclarationSyntax declaration, IReadOnlyList<string> baseTypes)
    {
        if (declaration is InterfaceDeclarationSyntax)
        {
            return IndexedTypeKind.Interface;
        }

        if (declaration is EnumDeclarationSyntax)
        {
            return IndexedTypeKind.Enum;
        }

        if (declaration is RecordDeclarationSyntax)
        {
            return IndexedTypeKind.Record;
        }

        if (declaration is StructDeclarationSyntax)
        {
            return IndexedTypeKind.Struct;
        }

        foreach (var baseType in baseTypes)
        {
            if (MonoBehaviourBases.Contains(baseType))
            {
                return IndexedTypeKind.MonoBehaviour;
            }

            if (ScriptableObjectBases.Contains(baseType))
            {
                return IndexedTypeKind.ScriptableObject;
            }
        }

        return IndexedTypeKind.Class;
    }

    private static List<IndexedMember> DescribeMembers(TypeDeclarationSyntax declaration)
    {
        var members = new List<IndexedMember>();

        foreach (var member in declaration.Members)
        {
            switch (member)
            {
                case MethodDeclarationSyntax method when IsVisible(method.Modifiers):
                    members.Add(new IndexedMember(
                        IndexedMemberKind.Method,
                        method.Identifier.ValueText,
                        $"{Modifiers(method.Modifiers)}{method.ReturnType} {method.Identifier}{method.TypeParameterList}{Parameters(method.ParameterList)}",
                        false));
                    break;

                case ConstructorDeclarationSyntax constructor when IsVisible(constructor.Modifiers):
                    members.Add(new IndexedMember(
                        IndexedMemberKind.Method,
                        constructor.Identifier.ValueText,
                        $"{Modifiers(constructor.Modifiers)}{constructor.Identifier}{Parameters(constructor.ParameterList)}",
                        false));
                    break;

                case PropertyDeclarationSyntax property when IsVisible(property.Modifiers):
                    members.Add(new IndexedMember(
                        IndexedMemberKind.Property,
                        property.Identifier.ValueText,
                        $"{Modifiers(property.Modifiers)}{property.Type} {property.Identifier} {{ {Accessors(property)} }}",
                        IsSerialized(property.Modifiers, property.AttributeLists)));
                    break;

                case EventFieldDeclarationSyntax eventField when IsVisible(eventField.Modifiers):
                    foreach (var variable in eventField.Declaration.Variables)
                    {
                        members.Add(new IndexedMember(
                            IndexedMemberKind.Event,
                            variable.Identifier.ValueText,
                            $"{Modifiers(eventField.Modifiers)}event {eventField.Declaration.Type} {variable.Identifier}",
                            false));
                    }

                    break;

                case FieldDeclarationSyntax field:
                {
                    var serialized = IsSerialized(field.Modifiers, field.AttributeLists);

                    // A private field Unity does not serialise is invisible to everything that
                    // matters here, so it is left out rather than spending budget on it.
                    if (!IsVisible(field.Modifiers) && !serialized)
                    {
                        break;
                    }

                    foreach (var variable in field.Declaration.Variables)
                    {
                        members.Add(new IndexedMember(
                            IndexedMemberKind.Field,
                            variable.Identifier.ValueText,
                            $"{Modifiers(field.Modifiers)}{field.Declaration.Type} {variable.Identifier}",
                            serialized));
                    }

                    break;
                }
            }
        }

        return members;
    }

    private static List<IndexedMember> DescribeEnumMembers(BaseTypeDeclarationSyntax declaration)
    {
        if (declaration is not EnumDeclarationSyntax enumDeclaration)
        {
            return new List<IndexedMember>();
        }

        return enumDeclaration.Members
            .Select(m => new IndexedMember(IndexedMemberKind.Field, m.Identifier.ValueText, m.Identifier.ValueText, false))
            .ToList();
    }

    /// <summary>
    /// Whether a member is part of the surface another file could use. Unity serialises private
    /// fields marked with an attribute, which is why those are treated separately.
    /// </summary>
    private static bool IsVisible(SyntaxTokenList modifiers)
        => modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword) || m.IsKind(SyntaxKind.InternalKeyword) || m.IsKind(SyntaxKind.ProtectedKeyword));

    /// <summary>
    /// Whether Unity will serialise this member, which decides whether renaming it loses data.
    /// A public field is serialised unless it opts out; a private one only opts in.
    /// </summary>
    private static bool IsSerialized(SyntaxTokenList modifiers, SyntaxList<AttributeListSyntax> attributes)
    {
        var names = attributes
            .SelectMany(list => list.Attributes)
            .Select(a => a.Name.ToString())
            .ToList();

        if (names.Any(n => n is "NonSerialized" or "System.NonSerialized" or "HideInInspector"))
        {
            return false;
        }

        if (names.Any(n => n is "SerializeField" or "UnityEngine.SerializeField" or "SerializeReference"))
        {
            return true;
        }

        return modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword))
               && !modifiers.Any(m => m.IsKind(SyntaxKind.StaticKeyword) || m.IsKind(SyntaxKind.ConstKeyword));
    }

    private static string Modifiers(SyntaxTokenList modifiers)
    {
        var kept = modifiers
            .Where(m => !m.IsKind(SyntaxKind.AsyncKeyword))
            .Select(m => m.ValueText)
            .ToList();

        return kept.Count == 0 ? string.Empty : string.Join(' ', kept) + " ";
    }

    private static string Parameters(ParameterListSyntax? list)
        => list is null
            ? "()"
            : "(" + string.Join(", ", list.Parameters.Select(p => $"{p.Type} {p.Identifier}")) + ")";

    private static string Accessors(PropertyDeclarationSyntax property)
    {
        if (property.AccessorList is null)
        {
            return "get;";
        }

        return string.Join(' ', property.AccessorList.Accessors.Select(a => a.Keyword.ValueText + ";"));
    }

    private static string NamespaceOf(SyntaxNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case NamespaceDeclarationSyntax declaration:
                    return declaration.Name.ToString();

                case FileScopedNamespaceDeclarationSyntax fileScoped:
                    return fileScoped.Name.ToString();
            }
        }

        return string.Empty;
    }

    private static string FirstNamespace(CompilationUnitSyntax root)
    {
        var declaration = root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
        return declaration?.Name.ToString() ?? string.Empty;
    }

    /// <summary>
    /// The type names a file mentions without declaring them. Used only to draw edges between
    /// files, so a few false positives cost nothing and a missed edge costs very little.
    /// </summary>
    private static List<string> CollectReferencedNames(CompilationUnitSyntax root, HashSet<string> declaredNames)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var identifier in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var name = identifier.Identifier.ValueText;

            if (name.Length < 3 || !char.IsUpper(name[0]) || declaredNames.Contains(name))
            {
                continue;
            }

            names.Add(name);
        }

        foreach (var baseType in root.DescendantNodes().OfType<BaseTypeSyntax>())
        {
            var name = baseType.Type.ToString();

            if (name.Length >= 3 && !declaredNames.Contains(name))
            {
                names.Add(name);
            }
        }

        return names.ToList();
    }
}
