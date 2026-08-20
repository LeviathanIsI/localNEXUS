using System.IO;
using LocalNEXUS.App.Services.ProjectIndex;

namespace LocalNEXUS.App.Services.Files;

/// <summary>
/// The rules a generated script has to obey before it is allowed near a Unity project.
/// </summary>
/// <remarks>
/// Unity does not bind scripts by class name. A scene or prefab stores the GUID out of the
/// script's <c>.cs.meta</c> file, and the serialized data inside it resolves by assembly plus
/// namespace plus class name. Several perfectly ordinary looking edits therefore destroy data
/// while compiling cleanly, and none of them announce themselves until a scene is opened.
///
/// Every rule here refuses. The alternative, warning and writing anyway, means the person finds
/// out when the prefab is already broken, and by then the information that would have explained
/// it has scrolled out of the feed.
///
/// Unity has changed its rename behaviour across versions, so the version in the opened project
/// is what the messages point at rather than a rule quoted from memory.
/// </remarks>
public static class UnityScriptRules
{
    /// <summary>
    /// Checks a file that is about to be written, and throws if it must not be.
    /// </summary>
    /// <param name="relativePath">Where the file will go, relative to the project root.</param>
    /// <param name="content">What will be written.</param>
    /// <param name="existing">What the index knows about the file today, or null when it is new.</param>
    /// <param name="declared">The types the new content declares.</param>
    /// <exception cref="UnityScriptRuleException">The write would break a binding.</exception>
    public static void Enforce(
        string relativePath,
        string content,
        IndexedFile? existing,
        IReadOnlyList<IndexedType> declared)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        EnforceFileNameMatchesBehaviour(relativePath, declared);

        if (existing is null)
        {
            return;
        }

        EnforceNoTypeDisappeared(relativePath, existing, declared);
        EnforceNoNamespaceChange(relativePath, existing, declared, content);
        EnforceNoSerializedFieldRenamed(relativePath, existing, declared, content);
        EnforceBehaviourStaysBehaviour(relativePath, existing, declared);
    }

    /// <summary>
    /// What to say in the feed after a new component is written. Generating a MonoBehaviour and
    /// never attaching it means it silently never runs, which looks exactly like a working run.
    /// </summary>
    public static string? DescribeAttachmentNeeded(IReadOnlyList<IndexedType> declared)
    {
        var behaviours = declared.Where(t => t.IsMonoBehaviour).Select(t => t.Name).ToList();

        if (behaviours.Count == 0)
        {
            return null;
        }

        return $"{string.Join(", ", behaviours)} will not run until it is attached to a GameObject in a scene or prefab. "
               + "Nothing here attaches it.";
    }

    /// <summary>
    /// A MonoBehaviour only binds when its file name matches its class name exactly. Unity will
    /// not report this as an error; the component simply refuses to be added.
    /// </summary>
    private static void EnforceFileNameMatchesBehaviour(string relativePath, IReadOnlyList<IndexedType> declared)
    {
        var behaviours = declared.Where(t => t.IsMonoBehaviour).ToList();

        if (behaviours.Count == 0)
        {
            return;
        }

        var fileName = Path.GetFileNameWithoutExtension(relativePath);

        if (behaviours.Any(t => string.Equals(t.Name, fileName, StringComparison.Ordinal)))
        {
            return;
        }

        var names = string.Join(", ", behaviours.Select(t => t.Name));

        throw new UnityScriptRuleException(
            $"{relativePath} declares the MonoBehaviour {names}, and Unity only binds a component when the file name "
            + $"matches its class name exactly. Name the file {behaviours[0].Name}.cs, or rename the class to {fileName}.");
    }

    /// <summary>
    /// A type that a scene may reference cannot simply stop existing. Renaming or moving one out
    /// of its file produces "The referenced script on this Behaviour is missing" everywhere it
    /// was used, and no compiler error at all.
    /// </summary>
    private static void EnforceNoTypeDisappeared(
        string relativePath,
        IndexedFile existing,
        IReadOnlyList<IndexedType> declared)
    {
        foreach (var was in existing.Types)
        {
            if (declared.Any(t => string.Equals(t.Name, was.Name, StringComparison.Ordinal)))
            {
                continue;
            }

            if (HasMovedFromShim(declared, was))
            {
                continue;
            }

            throw new UnityScriptRuleException(
                $"{relativePath} currently declares {was.Name} and the new content does not. "
                + "Scenes and prefabs reference a script by the GUID of its file and resolve the type by name, so removing "
                + $"or renaming it breaks every object using it with no compiler error. Keep {was.Name}, or add "
                + $"[MovedFrom(true, sourceClassName: \"{was.Name}\")] to the type that replaces it.");
        }
    }

    /// <summary>
    /// Serialized references resolve on namespace as well as name, so moving a type into or out
    /// of a namespace breaks them exactly as renaming it would.
    /// </summary>
    private static void EnforceNoNamespaceChange(
        string relativePath,
        IndexedFile existing,
        IReadOnlyList<IndexedType> declared,
        string content)
    {
        foreach (var was in existing.Types)
        {
            var now = declared.FirstOrDefault(t => string.Equals(t.Name, was.Name, StringComparison.Ordinal));

            if (now is null || string.Equals(now.Namespace, was.Namespace, StringComparison.Ordinal))
            {
                continue;
            }

            if (content.Contains("MovedFrom", StringComparison.Ordinal))
            {
                continue;
            }

            var from = was.Namespace.Length == 0 ? "the global namespace" : was.Namespace;
            var to = now.Namespace.Length == 0 ? "the global namespace" : now.Namespace;

            throw new UnityScriptRuleException(
                $"{relativePath} moves {was.Name} from {from} to {to}. Unity resolves a serialized reference by namespace "
                + "and class name together, so every scene and prefab using it would lose its script. Keep the namespace, or add "
                + $"[MovedFrom(true, sourceNamespace: \"{was.Namespace}\", sourceClassName: \"{was.Name}\")].");
        }
    }

    /// <summary>
    /// A serialized field that changes name loses whatever was set on it in every scene and
    /// prefab, unless it says what it used to be called.
    /// </summary>
    private static void EnforceNoSerializedFieldRenamed(
        string relativePath,
        IndexedFile existing,
        IReadOnlyList<IndexedType> declared,
        string content)
    {
        foreach (var was in existing.Types)
        {
            var now = declared.FirstOrDefault(t => string.Equals(t.Name, was.Name, StringComparison.Ordinal));

            if (now is null)
            {
                continue;
            }

            var kept = now.SerializedFields.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

            foreach (var field in was.SerializedFields)
            {
                if (kept.Contains(field.Name) || content.Contains($"FormerlySerializedAs(\"{field.Name}\")", StringComparison.Ordinal))
                {
                    continue;
                }

                throw new UnityScriptRuleException(
                    $"{relativePath} removes or renames the serialized field {was.Name}.{field.Name}. "
                    + "Unity stores serialized values by field name, so whatever is set on it in every scene and prefab would be lost. "
                    + $"Keep the field, or mark its replacement with [FormerlySerializedAs(\"{field.Name}\")].");
            }
        }
    }

    /// <summary>
    /// Taking MonoBehaviour off a type that instances are attached to compiles and breaks every
    /// one of them, because the component no longer binds.
    /// </summary>
    private static void EnforceBehaviourStaysBehaviour(
        string relativePath,
        IndexedFile existing,
        IReadOnlyList<IndexedType> declared)
    {
        foreach (var was in existing.Types.Where(t => t.IsMonoBehaviour))
        {
            var now = declared.FirstOrDefault(t => string.Equals(t.Name, was.Name, StringComparison.Ordinal));

            if (now is null || now.IsMonoBehaviour)
            {
                continue;
            }

            throw new UnityScriptRuleException(
                $"{relativePath} stops {was.Name} deriving from MonoBehaviour. Any GameObject with it attached would lose "
                + "the component, and nothing about that is a compiler error. Keep the base type.");
        }
    }

    private static bool HasMovedFromShim(IReadOnlyList<IndexedType> declared, IndexedType was)
        => declared.Any(t => t.BaseTypes.Contains(was.Name, StringComparer.Ordinal));
}
