namespace LocalNEXUS.Evals;

/// <summary>
/// The fixed set of tasks every run measures.
/// </summary>
/// <remarks>
/// Fixed is the operative word. The value of an eval is entirely in comparing this week's numbers
/// to last week's, and a task set that drifts makes every earlier result meaningless without
/// anybody noticing. So it is versioned, the version is written into every result, and changing
/// anything at all about a task, including its wording, means a new version.
///
/// The six shapes are chosen because each one fails differently. Writing a file from nothing tests
/// almost none of what this application does; referencing an existing type tests the index and the
/// ranking; editing tests whether the model can be trusted with something already working; a
/// multi file plan tests the ordering and the accumulated compile; the duplicate case tests the
/// one guard the whole design turns on; and the refusal case tests whether a change that compiles
/// cleanly and breaks a scene is caught.
///
/// The seed project is small on purpose. A large one would make the ranking the dominant variable
/// and this measures the whole pipeline, not the ranker.
/// </remarks>
public static class TaskSet
{
    /// <summary>
    /// The version of the task set. Bumped whenever any task changes in any way.
    /// </summary>
    /// <remarks>
    /// Written into every result. A result carrying a different version is not comparable and the
    /// summary says so rather than averaging across the two.
    /// </remarks>
    public const string Version = "1";

    private const string Scripts = "Assets/Scripts";

    /// <summary>The interface everything damageable implements, present in every seed.</summary>
    private static readonly SeedFile Damageable = new(
        $"{Scripts}/IDamageable.cs",
        """
        namespace Game
        {
            public interface IDamageable
            {
                void TakeDamage(int amount);
            }
        }
        """);

    /// <summary>An ordinary class implementing it, and the target of the edit task.</summary>
    private static readonly SeedFile Health = new(
        $"{Scripts}/Health.cs",
        """
        namespace Game
        {
            public class Health : IDamageable
            {
                private int _current = 100;

                public int Current => _current;

                public void TakeDamage(int amount)
                {
                    _current -= amount;
                }
            }
        }
        """);

    /// <summary>A plain type the duplicate guard should refuse a second copy of.</summary>
    private static readonly SeedFile InventorySlot = new(
        $"{Scripts}/InventorySlot.cs",
        """
        namespace Game
        {
            public class InventorySlot
            {
                public string ItemId;
                public int Count;
            }
        }
        """);

    /// <summary>A component with a serialized field, which is what makes renaming it dangerous.</summary>
    private static readonly SeedFile Spinner = new(
        $"{Scripts}/Spinner.cs",
        """
        using UnityEngine;

        namespace Game
        {
            public class Spinner : MonoBehaviour
            {
                [SerializeField]
                private float speed = 90f;

                private void Update()
                {
                    transform.Rotate(0f, speed * Time.deltaTime, 0f);
                }
            }
        }
        """);

    /// <summary>Every seed file. Each task starts from the same project so results compare.</summary>
    private static readonly IReadOnlyList<SeedFile> CommonSeed = new[]
    {
        Damageable,
        Health,
        InventorySlot,
        Spinner
    };

    /// <summary>The tasks, in a fixed order.</summary>
    public static IReadOnlyList<EvalTask> Tasks { get; } = new[]
    {
        // Nothing in the project is involved. Close to a floor: a model that cannot do this
        // cannot do any of the rest, and the number is worth having for that reason alone.
        new EvalTask(
            "new-file-alone",
            TaskShape.NewFileAlone,
            "Add a Cooldown class that tracks a duration in seconds, can be started, and reports whether it is ready.",
            CommonSeed,
            new[] { "Cooldown.cs" },
            Array.Empty<string>(),
            false,
            Array.Empty<string>()),

        // The new file has to call something the project already has, so the index and the
        // ranking are both in the path. A model given no context invents a plausible wrong name.
        new EvalTask(
            "new-file-references-existing",
            TaskShape.NewFileReferencingExisting,
            "Add a SpikeTrap MonoBehaviour that calls TakeDamage on anything implementing IDamageable when something enters its trigger.",
            CommonSeed,
            new[] { "SpikeTrap.cs" },
            Array.Empty<string>(),
            false,
            new[] { "IDamageable" }),

        // Changing something that already works. The failure mode is a rewrite that drops what
        // was there, which the guardrails catch and the file count does not.
        new EvalTask(
            "edit-existing",
            TaskShape.EditExisting,
            "Add a Heal method to the existing Health class that adds to the current value and does not exceed 100.",
            CommonSeed,
            Array.Empty<string>(),
            new[] { "Health.cs" },
            false,
            new[] { "Health" }),

        // Two files where the second calls the first. Written in parallel, the caller guesses the
        // name and is wrong, so this measures the ordering and the accumulated compile.
        new EvalTask(
            "multi-file-ordered",
            TaskShape.MultiFileOrdered,
            "Add an IInteractable interface with an Interact method, and a Door MonoBehaviour that implements it by toggling an open flag.",
            CommonSeed,
            new[] { "IInteractable.cs", "Door.cs" },
            Array.Empty<string>(),
            false,
            Array.Empty<string>()),

        // Phrased the way somebody who has forgotten what is already in their project phrases it.
        // The right answer is to edit InventorySlot, and the shortest path for a coder is a second
        // one. This is the single failure the application exists to prevent.
        new EvalTask(
            "should-edit-not-create",
            TaskShape.ShouldEditNotCreate,
            "I need an InventorySlot that holds an item id, a count, and a maximum stack size.",
            CommonSeed,
            Array.Empty<string>(),
            new[] { "InventorySlot.cs" },
            false,
            new[] { "InventorySlot" }),

        // Renaming a serialized field compiles cleanly and silently empties the value in every
        // scene the component is in. The write has to be refused, not warned about.
        new EvalTask(
            "unity-refusal",
            TaskShape.UnityRefusal,
            "Rename the speed field on Spinner to rotationSpeed.",
            CommonSeed,
            Array.Empty<string>(),
            new[] { "Spinner.cs" },
            true,
            new[] { "Spinner" })
    };

    /// <summary>The tasks whose identifiers match a filter, or all of them when it is empty.</summary>
    public static IReadOnlyList<EvalTask> Select(IReadOnlyCollection<string> ids)
        => ids.Count == 0
            ? Tasks
            : Tasks.Where(t => ids.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList();
}
