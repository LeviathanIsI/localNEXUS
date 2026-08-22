namespace LocalNEXUS.Evals;

/// <summary>
/// The tasks that run against an ordinary C# project.
/// </summary>
/// <remarks>
/// A second set rather than more rows on the first one. The Unity set scores whether the right
/// rule refused the right edit; this set scores the opposite, that none of those rules fired at
/// all, and half of the Unity criteria are meaningless here. Mixing them would produce a table
/// where half the columns mean nothing for half the rows.
///
/// What it shares is the harness, the graph, the measurement and the report. What differs is the
/// project it builds, which is a csproj and a src folder with nothing Unity would recognise, and
/// the bar, which counts a Unity refusal as a defect rather than as a score.
///
/// The seed is a small shop library, chosen because it gives real relationships without needing a
/// domain anybody has to learn: an interface with an implementation so ordering matters, a type
/// that holds a list of another so a reference is genuine, a value type that a request could
/// plausibly ask to be created a second time, and a type carrying a public field and a namespace,
/// which are the two things the Unity rules refuse to let move.
/// </remarks>
public static class PlainTaskSet
{
    /// <summary>
    /// Which set of tasks these are.
    /// </summary>
    /// <remarks>
    /// Its own line rather than a number, because a plain result and a Unity result are not
    /// comparable and a bare version would let them be read as though they were.
    /// </remarks>
    public const string Version = "plain-1";

    private const string Source = "src";

    private static readonly SeedFile PricingRule = new(
        $"{Source}/IPricingRule.cs",
        """
        namespace Shop
        {
            public interface IPricingRule
            {
                decimal Apply(decimal subtotal);
            }
        }
        """);

    private static readonly SeedFile PercentageDiscount = new(
        $"{Source}/PercentageDiscount.cs",
        """
        namespace Shop
        {
            public class PercentageDiscount : IPricingRule
            {
                private readonly decimal _percent;

                public PercentageDiscount(decimal percent)
                {
                    _percent = percent;
                }

                public decimal Apply(decimal subtotal)
                {
                    return subtotal - (subtotal * _percent / 100m);
                }
            }
        }
        """);

    /// <summary>A value type, and the one a request could plausibly ask for a second copy of.</summary>
    private static readonly SeedFile Money = new(
        $"{Source}/Money.cs",
        """
        namespace Shop
        {
            public readonly struct Money
            {
                public Money(decimal amount, string currency)
                {
                    Amount = amount;
                    Currency = currency;
                }

                public decimal Amount { get; }

                public string Currency { get; }

                public Money Add(Money other)
                {
                    return new Money(Amount + other.Amount, Currency);
                }
            }
        }
        """);

    private static readonly SeedFile BasketItem = new(
        $"{Source}/BasketItem.cs",
        """
        namespace Shop
        {
            public class BasketItem
            {
                public string Sku;
                public int Quantity;
                public decimal UnitPrice;
            }
        }
        """);

    private static readonly SeedFile Basket = new(
        $"{Source}/Basket.cs",
        """
        using System.Collections.Generic;

        namespace Shop
        {
            public class Basket
            {
                private readonly List<BasketItem> _items = new List<BasketItem>();

                public IReadOnlyList<BasketItem> Items => _items;

                public void Add(BasketItem item)
                {
                    _items.Add(item);
                }

                public decimal Subtotal()
                {
                    decimal total = 0m;

                    foreach (var item in _items)
                    {
                        total += item.UnitPrice * item.Quantity;
                    }

                    return total;
                }
            }
        }
        """);

    /// <summary>
    /// The type the Unity rules would refuse to let move.
    /// </summary>
    /// <remarks>
    /// A public instance field, which the Unity parser counts as serialized whatever project it is
    /// in, and a namespace. Renaming the field trips SerializedFieldMayNotBeRenamed in a Unity
    /// project and moving the type trips NamespaceMayNotChange. Neither may fire here.
    /// </remarks>
    private static readonly SeedFile Coupon = new(
        $"{Source}/Coupon.cs",
        """
        namespace Shop
        {
            public class Coupon
            {
                public string Code;
                public decimal Amount;

                public bool Matches(string entered)
                {
                    return Code == entered;
                }
            }
        }
        """);

    private static readonly IReadOnlyList<SeedFile> Seed = new[]
    {
        PricingRule, PercentageDiscount, Money, BasketItem, Basket, Coupon
    };

    /// <summary>
    /// Ten tasks, covering what an ordinary codebase actually does.
    /// </summary>
    /// <remarks>
    /// Two of them are renames a Unity project would refuse, and that is deliberate rather than
    /// redundant. There are two separate rules involved, one about a serialized field and one about
    /// a namespace, and proving that one of them is correctly scoped proves nothing about the
    /// other.
    ///
    /// The first task is the only one whose file can compile against the framework alone, because
    /// it references nothing the project declares. That is not an accident of the writing; it is
    /// the thing this set exists to quantify, and having one task that can be proven is what makes
    /// the others reading inconclusive mean something.
    /// </remarks>
    public static IReadOnlyList<EvalTask> Tasks { get; } = new[]
    {
        // Nothing to reference, so the compile check can actually prove this one.
        new EvalTask(
            "plain-new-file-alone",
            TaskShape.NewFileAlone,
            "Add a Slug class with a static method that turns a product name into a lowercase url slug.",
            Seed,
            new[] { "Slug.cs" },
            Array.Empty<string>(),
            false,
            new[] { "Basket", "Money", "Coupon" },
            Project: ProjectShape.Plain),

        new EvalTask(
            "plain-new-file-references-existing",
            TaskShape.NewFileReferencingExisting,
            "Add a BuyOneGetOneFree pricing rule that implements IPricingRule.",
            Seed,
            new[] { "BuyOneGetOneFree.cs" },
            Array.Empty<string>(),
            false,
            new[] { "IPricingRule", "PercentageDiscount" },
            TypeThatShouldBeReused: "Shop.IPricingRule",
            Project: ProjectShape.Plain),

        new EvalTask(
            "plain-edit-existing",
            TaskShape.EditExisting,
            "Add a Clear method to Basket that empties it.",
            Seed,
            Array.Empty<string>(),
            new[] { "Basket.cs" },
            false,
            new[] { "Basket" },
            TypeThatShouldBeReused: "Shop.Basket",
            Project: ProjectShape.Plain),

        new EvalTask(
            "plain-multi-file-ordered",
            TaskShape.MultiFileOrdered,
            "Add an IShippingRate interface with a Quote method, and a FlatRate class that implements it.",
            Seed,
            new[] { "IShippingRate.cs", "FlatRate.cs" },
            Array.Empty<string>(),
            false,
            new[] { "IPricingRule", "Basket" },
            Project: ProjectShape.Plain),

        // Phrased the way somebody would phrase it, which never says the word edit.
        new EvalTask(
            "plain-should-edit-not-create",
            TaskShape.ShouldEditNotCreate,
            "Baskets need to be able to report how many items are in them.",
            Seed,
            Array.Empty<string>(),
            new[] { "Basket.cs" },
            false,
            new[] { "Basket", "BasketItem" },
            TypeThatShouldBeReused: "Shop.Basket",
            Project: ProjectShape.Plain),

        // Money already adds. The right answer is to write nothing at all.
        new EvalTask(
            "plain-change-nothing",
            TaskShape.ChangeNothing,
            "Make sure two Money values can be added together.",
            Seed,
            Array.Empty<string>(),
            Array.Empty<string>(),
            false,
            new[] { "Money" },
            TypeThatShouldBeReused: "Shop.Money",
            ExpectsNoChange: true,
            Project: ProjectShape.Plain),

        new EvalTask(
            "plain-ambiguous-request",
            TaskShape.Ambiguous,
            "Make it faster.",
            Seed,
            Array.Empty<string>(),
            Array.Empty<string>(),
            false,
            new[] { "Basket", "Money", "Coupon" },
            ExpectsClarification: true,
            Project: ProjectShape.Plain),

        // A Unity project refuses this outright, because any public instance field counts as
        // serialized and renaming one loses whatever was set on it in every scene. Here it is a
        // rename.
        new EvalTask(
            "plain-rename-field-allowed",
            TaskShape.AllowedRename,
            "Rename the Code field on Coupon to CouponCode.",
            Seed,
            Array.Empty<string>(),
            new[] { "Coupon.cs" },
            false,
            new[] { "Coupon" },
            TypeThatShouldBeReused: "Shop.Coupon",
            Project: ProjectShape.Plain),

        // And a Unity project refuses this one too, because a serialized reference resolves by
        // namespace and class name together.
        new EvalTask(
            "plain-namespace-move-allowed",
            TaskShape.AllowedRename,
            "Move the Coupon class into a Shop.Promotions namespace.",
            Seed,
            Array.Empty<string>(),
            new[] { "Coupon.cs" },
            false,
            new[] { "Coupon" },
            TypeThatShouldBeReused: "Shop.Coupon",
            Project: ProjectShape.Plain),

        // Money is already there. A second one is the failure this application exists to prevent,
        // and it is prevented the same way in any codebase.
        new EvalTask(
            "plain-duplicate-refused",
            TaskShape.DuplicateAttempt,
            "Add a Money type that holds an amount and a currency.",
            Seed,
            Array.Empty<string>(),
            Array.Empty<string>(),
            false,
            new[] { "Money" },
            TypeThatShouldBeReused: "Shop.Money",
            ExpectsNoChange: true,
            Project: ProjectShape.Plain)
    };
}
