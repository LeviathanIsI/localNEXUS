using LocalNEXUS.App.Services.Files;

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
    ///
    /// Version two: the requests and the seed project are word for word what version one had, and
    /// the tasks now also state which existing type the right answer reuses and which rule ought
    /// to refuse the write. Nothing a model is shown changed, but what is scored did, so results
    /// from version one cannot be compared on the duplicate or refusal columns and the version is
    /// what says so.
    ///
    /// Version three: fourteen tasks added and the original six left exactly as they were, seed
    /// project included, so a per task comparison with a version two result is still valid for
    /// those six. A total across the set is not, because the denominator changed from six to
    /// twenty and the fourteen are deliberately harder.
    /// </remarks>
    public const string Version = "3";

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

    /// <summary>An asset rather than a component, so the rules are exercised on a ScriptableObject.</summary>
    private static readonly SeedFile WeaponData = new(
        $"{Scripts}/WeaponData.cs",
        """
        using UnityEngine;

        namespace Game
        {
            [CreateAssetMenu(menuName = "Game/Weapon")]
            public class WeaponData : ScriptableObject
            {
                [SerializeField]
                private string displayName = "Sword";

                public string DisplayName => displayName;
            }
        }
        """);

    /// <summary>A second thing implementing IDamageable, so two files can need the same change.</summary>
    private static readonly SeedFile Enemy = new(
        $"{Scripts}/Enemy.cs",
        """
        namespace Game
        {
            public class Enemy : IDamageable
            {
                private int _hitPoints = 30;

                public int HitPoints => _hitPoints;

                public void TakeDamage(int amount)
                {
                    _hitPoints -= amount;
                }
            }
        }
        """);

    /// <summary>A helper that already does the thing one task asks for.</summary>
    private static readonly SeedFile MathUtil = new(
        $"{Scripts}/MathUtil.cs",
        """
        namespace Game
        {
            public static class MathUtil
            {
                public static int Clamp(int value, int minimum, int maximum)
                {
                    if (value < minimum)
                    {
                        return minimum;
                    }

                    return value > maximum ? maximum : value;
                }
            }
        }
        """);

    /// <summary>Everything the added tasks may need, on top of what the original six see.</summary>
    /// <remarks>
    /// A separate list on purpose. The original six keep the project they were measured against,
    /// so a per task comparison with an older result still means something; anything added since
    /// gets the larger one.
    /// </remarks>
    private static readonly IReadOnlyList<SeedFile> ExtendedSeed = new[]
    {
        Damageable,
        Health,
        InventorySlot,
        Spinner,
        WeaponData,
        Enemy,
        MathUtil
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
            new[] { "Health" },
            TypeThatShouldBeReused: "Game.Health"),

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
            new[] { "InventorySlot" },
            TypeThatShouldBeReused: "Game.InventorySlot"),

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
            new[] { "Spinner" },
            TypeThatShouldBeReused: "Game.Spinner",
            AcceptableRefusalRules: new[] { nameof(ProjectWriteRule.SerializedFieldMayNotBeRenamed) }),

        // Three files where two of them implement the third. The two file case already passes, so
        // this asks whether the ordering holds when the dependency has more than one dependant.
        new EvalTask(
            "interface-two-implementations",
            TaskShape.InterfaceWithImplementations,
            "Add an IPickup interface with an OnPickedUp method, and two MonoBehaviours that implement it: HealthPickup and AmmoPickup.",
            ExtendedSeed,
            new[] { "IPickup.cs", "HealthPickup.cs", "AmmoPickup.cs" },
            Array.Empty<string>(),
            false,
            Array.Empty<string>()),

        // Everything else in the set is a component or a plain class. An asset is bound by the
        // same serialization rules and nothing had ever put one in front of them.
        new EvalTask(
            "scriptable-object",
            TaskShape.ScriptableObject,
            "Add a serialized damage field to the WeaponData ScriptableObject, with a public property that reads it.",
            ExtendedSeed,
            Array.Empty<string>(),
            new[] { "WeaponData.cs" },
            false,
            new[] { "WeaponData" },
            TypeThatShouldBeReused: "Game.WeaponData"),

        // The project already does this. Writing anything at all is the failure, and it is the one
        // shape where a productive looking model is the wrong model.
        new EvalTask(
            "change-nothing",
            TaskShape.ChangeNothing,
            "Make sure MathUtil has a Clamp method that limits an integer between a minimum and a maximum.",
            ExtendedSeed,
            Array.Empty<string>(),
            Array.Empty<string>(),
            false,
            new[] { "MathUtil" },
            ExpectsNoChange: true),

        // Nothing in the project is called a player and nothing says what faster means. Guessing
        // produces a confidently wrong answer, which is what the elicitation exists to prevent.
        new EvalTask(
            "ambiguous-request",
            TaskShape.Ambiguous,
            "Make it faster.",
            ExtendedSeed,
            Array.Empty<string>(),
            Array.Empty<string>(),
            false,
            Array.Empty<string>(),
            ExpectsClarification: true),

        // Two existing files need the same change. A plan that does one and forgets the other
        // leaves the project half changed and compiling.
        new EvalTask(
            "edit-two-files",
            TaskShape.EditTwoFiles,
            "Add an IsDead property to both Health and Enemy that reports whether the remaining value has reached zero.",
            ExtendedSeed,
            Array.Empty<string>(),
            new[] { "Health.cs", "Enemy.cs" },
            false,
            new[] { "Health", "Enemy" },
            TypeThatShouldBeReused: "Game.Health"),

        // The shortest path is a new HealthSettings beside the thing it configures. The right
        // answer is to put the value on the class that owns it.
        new EvalTask(
            "extend-not-sibling",
            TaskShape.ShouldEditNotCreate,
            "Health needs an upper limit that healing cannot go past, and it should be settable.",
            ExtendedSeed,
            Array.Empty<string>(),
            new[] { "Health.cs" },
            false,
            new[] { "Health" },
            TypeThatShouldBeReused: "Game.Health"),

        // A scene references a script by the GUID of its file and resolves the type by name, so a
        // class that stops existing under that name breaks every object using it, silently.
        new EvalTask(
            "rename-bound-class",
            TaskShape.UnityRefusal,
            "Rename the Spinner class to Rotator.",
            ExtendedSeed,
            Array.Empty<string>(),
            new[] { "Spinner.cs" },
            true,
            new[] { "Spinner" },
            TypeThatShouldBeReused: "Game.Spinner",
            AcceptableRefusalRules: new[]
            {
                nameof(ProjectWriteRule.TypeMayNotDisappear),
                nameof(ProjectWriteRule.FileNameMustMatchBehaviour)
            }),

        // The same rename the refusal task asks for, with the escape hatch named. This one is
        // supposed to succeed, and it is what says the rules are a fence rather than a wall.
        new EvalTask(
            "serialized-rename-with-shim",
            TaskShape.EditExisting,
            "Rename the speed field on Spinner to rotationSpeed, and add a FormerlySerializedAs attribute so scenes keep the value they already have.",
            ExtendedSeed,
            Array.Empty<string>(),
            new[] { "Spinner.cs" },
            false,
            new[] { "Spinner" },
            TypeThatShouldBeReused: "Game.Spinner"),

        // Ten types in one request. The budget is stated in characters and the signatures of
        // everything written so far have to fit inside part of it, so this is where that runs out.
        new EvalTask(
            "oversized-plan",
            TaskShape.OversizedPlan,
            "Add a complete inventory system: an Item class, an ItemStack class, an Inventory class, "
            + "an IItemContainer interface, an ItemDatabase ScriptableObject, an EquipmentSlot enum, "
            + "an InventoryEvents static class, a PickupSpawner MonoBehaviour, an InventorySaveData class, "
            + "and an InventoryUI MonoBehaviour.",
            ExtendedSeed,
            new[] { "Item.cs", "ItemStack.cs", "Inventory.cs" },
            Array.Empty<string>(),
            false,
            new[] { "InventorySlot" }),

        // ITimeSource exists nowhere. Assuming it does produces a file that cannot compile and a
        // repair loop chasing a type that was never there.
        new EvalTask(
            "missing-type-must-create",
            TaskShape.MissingDependency,
            "Add a LapTimer class that reads the current time from an ITimeSource interface and records lap durations.",
            ExtendedSeed,
            new[] { "LapTimer.cs", "ITimeSource.cs" },
            Array.Empty<string>(),
            false,
            Array.Empty<string>()),

        // Volume. Two plain requests with nothing clever in them, because a set made entirely of
        // hard cases does not say what an ordinary day looks like.
        new EvalTask(
            "routine-enum",
            TaskShape.Routine,
            "Add a DamageType enum with Physical, Fire and Poison values.",
            ExtendedSeed,
            new[] { "DamageType.cs" },
            Array.Empty<string>(),
            false,
            Array.Empty<string>()),

        new EvalTask(
            "routine-utility",
            TaskShape.Routine,
            "Add a static StringUtil class with a method that turns a camelCase string into words separated by spaces.",
            ExtendedSeed,
            new[] { "StringUtil.cs" },
            Array.Empty<string>(),
            false,
            Array.Empty<string>()),

        // Chosen because every file this set has ever written landed in one flat folder, so
        // nothing had exercised creating a directory or resolving a nested path.
        new EvalTask(
            "nested-folder",
            TaskShape.NewFileAlone,
            "Add a CombatLog class under Assets/Scripts/Combat that records the last ten damage events.",
            ExtendedSeed,
            new[] { "CombatLog.cs" },
            Array.Empty<string>(),
            false,
            Array.Empty<string>()),

        // Chosen because the set trips two of the seven write rules and this is a third. Unity
        // resolves a serialized reference by namespace and class name together, so moving a type
        // between namespaces loses the script on every scene using it.
        new EvalTask(
            "namespace-move-refused",
            TaskShape.UnityRefusal,
            "Move the InventorySlot class into a Game.Inventory namespace.",
            ExtendedSeed,
            Array.Empty<string>(),
            new[] { "InventorySlot.cs" },
            true,
            new[] { "InventorySlot" },
            TypeThatShouldBeReused: "Game.InventorySlot",
            AcceptableRefusalRules: new[] { nameof(ProjectWriteRule.NamespaceMayNotChange) })
    };

    /// <summary>The tasks whose identifiers match a filter, or all of them when it is empty.</summary>
    public static IReadOnlyList<EvalTask> Select(IReadOnlyCollection<string> ids)
        => ids.Count == 0
            ? Tasks
            : Tasks.Where(t => ids.Contains(t.Id, StringComparer.OrdinalIgnoreCase)).ToList();
}
