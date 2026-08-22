using LocalNEXUS.App.Services.Debate;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// What the convergence meter counts as a name.
/// </summary>
/// <remarks>
/// A word is an identifier because the open project has a type or a file by that name. It is not
/// an identifier because it looks like one.
///
/// That replaced a pattern that tried to tell the difference by looking. Every guard added to it
/// revealed another class of ordinary word: it began by missing Health and Door entirely, and when
/// single words were allowed through it produced Existing, Integration, Practice, Reusability,
/// Support and Usability as the names a debate was about. There is no pattern separating those
/// from Health, because there is no difference to see. There is a list of what the project
/// contains, and it is exact.
///
/// Two shapes are taken whatever the project holds, because nothing else can produce them: a
/// backticked span is somebody saying this is code, and the IThing convention is not English. They
/// are what lets a debate about a type nobody has written yet still be measured.
///
/// Every test reads the identifiers back out of a measurement of a position against itself, since
/// everything named is then shared.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class IdentifierExtractionTests
{
    /// <summary>A project that contains the things a Unity project contains.</summary>
    private static readonly string[] Project =
    {
        "Health", "Door", "Spinner", "Enemy", "InventorySlot", "IDamageable",
        "TakeDamage", "Interact", "Update",
        "Health.cs", "Assets/Scripts/Door.cs"
    };

    private static IReadOnlyList<string> Extract(string text, params string[] known)
        => ConvergenceMeter.Measure(text, text, known.Length == 0 ? Project : known).SharedIdentifiers;

    private static void Finds(string text, params string[] expected)
    {
        var found = Extract(text);

        foreach (var name in expected)
        {
            Assert.Contains(name, found);
        }
    }

    private static void DoesNotFind(string text, params string[] rejected)
    {
        var found = Extract(text);

        foreach (var name in rejected)
        {
            Assert.DoesNotContain(name, found);
        }
    }

    /// <summary>A single word type the project has is found.</summary>
    /// <remarks>
    /// What was missing before. Health, Door and Spinner are the ordinary case and none of them
    /// counted, because a pattern looking for two humps cannot see them.
    /// </remarks>
    [Theory]
    [InlineData("Health")]
    [InlineData("Door")]
    [InlineData("Spinner")]
    [InlineData("Enemy")]
    public void ASingleWordTypeTheProjectHasIsFound(string name)
        => Finds($"The change belongs on {name} and nowhere else.", name);

    /// <summary>It is found wherever it appears, including opening a sentence.</summary>
    /// <remarks>
    /// Position stops mattering once the project decides the question. A guard that refused a
    /// capitalised word at the start of a sentence used to be necessary and threw away the subject
    /// of every sentence of the form "Door implements IInteractable".
    /// </remarks>
    [Fact]
    public void APositionInTheSentenceNoLongerMatters()
        => Finds("Door implements IInteractable. Health does not.", "Door", "Health");

    /// <summary>A method the project has is found.</summary>
    [Theory]
    [InlineData("TakeDamage")]
    [InlineData("Interact")]
    [InlineData("Update")]
    public void AMemberTheProjectHasIsFound(string name)
        => Finds($"It should call {name} when something enters the trigger.", name);

    /// <summary>A file the project has is found, by name or by path.</summary>
    [Fact]
    public void AFileTheProjectHasIsFound()
    {
        Finds("Put it in Assets/Scripts/Door.cs rather than beside it.", "Assets/Scripts/Door.cs");
        Finds("Change Health.cs instead.", "Health.cs");
    }

    /// <summary>
    /// An interface in the IThing convention is found whether the project has it or not.
    /// </summary>
    /// <remarks>
    /// One of the two shapes that need no corroborating. A capital I, another capital, then a
    /// lowercase run is not something English produces, so it is safe to take on sight, which is
    /// what makes a debate about a type nobody has created yet measurable.
    /// </remarks>
    [Theory]
    [InlineData("IDamageable")]
    [InlineData("IInteractable")]
    [InlineData("IItemContainer")]
    public void AnInterfaceInTheIThingConventionIsFound(string name)
        => Finds($"Anything implementing {name} should handle it.", name);

    /// <summary>A backticked span is found whether the project has it or not.</summary>
    /// <remarks>
    /// The other unambiguous shape, and the way to name something that does not exist yet and is
    /// not an interface.
    /// </remarks>
    [Fact]
    public void ABacktickedSpanIsFound()
        => Finds("We would add a `StackLimit` to it.", "StackLimit");

    /// <summary>
    /// A word the project does not have is not a name, however much it looks like one.
    /// </summary>
    /// <remarks>
    /// The whole of the change. Every one of these was produced as an identifier by the pattern
    /// that came before, from a real debate, and not one of them is the name of anything.
    /// </remarks>
    [Theory]
    [InlineData("Existing")]
    [InlineData("Integration")]
    [InlineData("Practice")]
    [InlineData("Reusability")]
    [InlineData("Support")]
    [InlineData("Usability")]
    public void AWordTheProjectDoesNotHaveIsNotAName(string word)
        => DoesNotFind($"{word} matters more than anything else, and {word} is why.", word);

    /// <summary>Ordinary English is not a name either, which now needs no list to establish.</summary>
    [Theory]
    [InlineData("However")]
    [InlineData("Unity")]
    [InlineData("Performance")]
    [InlineData("Better")]
    public void OrdinaryEnglishIsNotAName(string word)
        => DoesNotFind($"{word} is the thing that decides it here.", word);

    /// <summary>
    /// Matching is case sensitive, so an ordinary word is not the type that shares its spelling.
    /// </summary>
    /// <remarks>
    /// A project with a Door in it does not turn every mention of a door into a reference to the
    /// type.
    /// </remarks>
    [Fact]
    public void TheOrdinaryWordIsNotTheType()
    {
        DoesNotFind("The door should open when the health runs out.", "Door", "Health");
        Finds("Door should open when Health runs out.", "Door", "Health");
    }

    /// <summary>Plural normalisation still holds, on both sides of the match.</summary>
    [Fact]
    public void SingularAndPluralAreStillOneIdentifier()
    {
        var measured = ConvergenceMeter.Measure(
            "Give Door a latch, keep Spinner, and leave Enemy alone.",
            "Give Doors a latch, keep Spinner, and leave Enemy alone.",
            Project);

        Assert.True(measured.IsMeasured);
        Assert.Contains("Door", measured.SharedIdentifiers);
        Assert.Empty(measured.FirstOnlyIdentifiers);
        Assert.Empty(measured.SecondOnlyIdentifiers);
    }

    /// <summary>With no project open, only the two unambiguous shapes count.</summary>
    /// <remarks>
    /// This is what a debate about something that does not exist yet looks like, and it is why
    /// those two shapes are kept.
    /// </remarks>
    [Fact]
    public void WithNoProjectOnlyTheUnambiguousShapesCount()
    {
        var measured = ConvergenceMeter.Measure(
            "IDamageable should own it, and `StackLimit` belongs beside it. Health does not.",
            "IDamageable should own it, and `StackLimit` belongs beside it. Health does not.");

        Assert.Contains("IDamageable", measured.SharedIdentifiers);
        Assert.Contains("StackLimit", measured.SharedIdentifiers);
        Assert.DoesNotContain("Health", measured.SharedIdentifiers);
    }

    /// <summary>
    /// A real code debate clears the floor it used to fall under.
    /// </summary>
    /// <remarks>
    /// Two positions naming an interface and two single word types were worth one identifier
    /// between them under the old pattern, which was below the v1.29 floor and therefore
    /// unmeasurable, even though a person can see exactly what is being discussed.
    /// </remarks>
    [Fact]
    public void ACodeDebateThatUsedToBeUnmeasurableNowScores()
    {
        var measured = ConvergenceMeter.Measure(
            "Health should implement IDamageable, and Door should call TakeDamage on it.",
            "Health should implement IDamageable, and Door should call TakeDamage on it.",
            Project);

        Assert.True(measured.IsMeasured);
        Assert.Equal(100, measured.Score);
        Assert.Contains("Health", measured.SharedIdentifiers);
        Assert.Contains("IDamageable", measured.SharedIdentifiers);
        Assert.Contains("Door", measured.SharedIdentifiers);
        Assert.Contains("TakeDamage", measured.SharedIdentifiers);
    }
}
