using LocalNEXUS.App.Services.Debate;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Measuring how far apart two positions are, without asking a model.
/// </summary>
/// <remarks>
/// This replaced a scored model call, which cost a request per round and gave a different answer
/// each time it was asked the same question. What it does instead is arithmetic on a handful of
/// words, weighted so that naming the same things counts for most, proposing the same actions
/// counts for the rest, and filler counts for nothing.
///
/// The cases here are the ones the weighting has to get right for the number to mean anything: the
/// same content in different prose has to score high, different content in similar prose has to
/// score low, and too little of either has to produce no number at all.
///
/// Every position below names at least three things, because that is now the floor beneath which
/// nothing is scored. The older versions of these tests used one or two and were measuring a share
/// of a sample too small to have one.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ConvergenceTests
{
    /// <summary>
    /// What the project in these tests contains.
    /// </summary>
    /// <remarks>
    /// A word is a name because the project has one by that name, so a test measuring positions
    /// about InventorySlot has to say that InventorySlot exists. Passing it explicitly is also what
    /// keeps these deterministic: nothing here reads a real project.
    /// </remarks>
    private static readonly string[] Project =
    {
        "InventorySlot", "HealthBar", "ItemStack", "ProjectileSpawner", "WeaponSlot",
        "DamageType", "ItemClass", "GameStatus", "ScriptableObject", "Spinner", "ItemId"
    };

    [Fact]
    public void IdenticalPositionsScoreTop()
    {
        const string position = "Put stacking on InventorySlot. HealthBar reads from ItemStack.";

        var measured = ConvergenceMeter.Measure(position, position, Project);

        Assert.Equal(100, measured.Score);
        Assert.Empty(measured.Contradictions);
    }

    /// <summary>
    /// The same proposal in different words scores high, because prose is not the measurement.
    /// </summary>
    /// <remarks>
    /// The case that broke the first attempt. Two models saying the same thing in their own voice
    /// scored ten, because the words did not match, and every debate ran to its cap.
    /// </remarks>
    [Fact]
    public void TheSameProposalInDifferentProseScoresHigh()
    {
        var measured = ConvergenceMeter.Measure(
            "I would add stacking to InventorySlot, and have HealthBar read from ItemStack.",
            "Stacking belongs on InventorySlot. HealthBar should read ItemStack as well.", Project);

        Assert.NotNull(measured.Score);
        Assert.True(measured.Score >= 60, $"scored {measured.Score}: {measured.Breakdown()}");
        Assert.Empty(measured.Contradictions);
    }

    /// <summary>Different proposals in similar prose score low, because the nouns differ.</summary>
    [Fact]
    public void DifferentProposalsInSimilarProseScoreLow()
    {
        var measured = ConvergenceMeter.Measure(
            "I would add stacking to InventorySlot and have HealthBar read ItemStack.",
            "I would add pooling to ProjectileSpawner and have WeaponSlot read DamageType.", Project);

        Assert.NotNull(measured.Score);
        Assert.True(measured.Score <= 30, $"scored {measured.Score}: {measured.Breakdown()}");
    }

    /// <summary>
    /// Naming the same thing and wanting opposite things done to it is a contradiction.
    /// </summary>
    /// <remarks>
    /// Two sides both talking about InventorySlot look like agreement to anything counting shared
    /// nouns, which is why the contradiction penalty exists and why it is not a small one.
    /// </remarks>
    [Fact]
    public void OppositeIntentionsAboutTheSameThingArePenalised()
    {
        var agreeing = ConvergenceMeter.Measure(
            "Add stacking to InventorySlot. Keep HealthBar and ItemStack as they are.",
            "Add stacking to InventorySlot. Keep HealthBar and ItemStack as they are.", Project);

        var opposed = ConvergenceMeter.Measure(
            "Add stacking to InventorySlot. Keep HealthBar and ItemStack as they are.",
            "Remove stacking from InventorySlot. Keep HealthBar and ItemStack as they are.", Project);

        Assert.NotNull(agreeing.Score);
        Assert.NotNull(opposed.Score);
        Assert.True(opposed.Score < agreeing.Score, $"opposed {opposed.Score} against agreeing {agreeing.Score}");
    }

    /// <summary>
    /// A verb in one sentence does not attach to a noun in another.
    /// </summary>
    /// <remarks>
    /// Found by measurement rather than by reading. An attachment window of a few characters either
    /// side turned every verb into a claim about every nearby identifier, so two positions that
    /// agreed produced three contradictions and scored ten. Attachment is scoped to the sentence.
    /// </remarks>
    [Fact]
    public void AVerbDoesNotReachIntoTheNextSentence()
    {
        var measured = ConvergenceMeter.Measure(
            "Add stacking to InventorySlot. Do not remove HealthBar or ItemStack.",
            "Add stacking to InventorySlot. Do not remove HealthBar or ItemStack.", Project);

        Assert.Empty(measured.Contradictions);
        Assert.Equal(100, measured.Score);
    }

    /// <summary>Two positions with nothing concrete in them cannot be measured, and say so.</summary>
    [Fact]
    public void NothingConcreteIsNotMeasurable()
    {
        var measured = ConvergenceMeter.Measure("I agree.", "So do I.", Project);

        Assert.Null(measured.Score);
        Assert.Equal("not measurable", measured.Text);
    }

    [Fact]
    public void AnEmptyPositionIsNotMeasurable()
        => Assert.Null(ConvergenceMeter.Measure(string.Empty, "something concrete about InventorySlot", Project).Score);

    /// <summary>The score is symmetric, because neither side is the reference.</summary>
    [Fact]
    public void TheOrderOfTheTwoDoesNotMatter()
    {
        const string a = "Add stacking to InventorySlot, pooling to ProjectileSpawner, and read ItemStack.";
        const string b = "Add pooling to ProjectileSpawner. Leave InventorySlot and WeaponSlot alone.";

        Assert.Equal(ConvergenceMeter.Measure(a, b, Project).Score, ConvergenceMeter.Measure(b, a, Project).Score);
    }

    /// <summary>
    /// The working is shown, so the number can be argued with.
    /// </summary>
    /// <remarks>
    /// A convergence score decides whether a debate stops. A number with no visible derivation is
    /// one nobody can tell is wrong, and this one is arithmetic on a handful of words.
    /// </remarks>
    [Fact]
    public void TheBreakdownNamesWhatItCounted()
    {
        var measured = ConvergenceMeter.Measure(
            "Add stacking to InventorySlot, and leave HealthBar alone.",
            "Add pooling to ProjectileSpawner, and rewrite WeaponSlot.", Project);

        var breakdown = measured.Breakdown();

        Assert.False(string.IsNullOrWhiteSpace(breakdown));
        Assert.Contains("InventorySlot", breakdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ProjectileSpawner", breakdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The weighting is stated where anyone reading a score can find it.</summary>
    [Fact]
    public void TheWeightingIsStated()
    {
        Assert.False(string.IsNullOrWhiteSpace(ConvergenceMeter.WeightingSummary));
        Assert.Equal(1.0d, ConvergenceMeter.IdentifierWeight + ConvergenceMeter.IntentWeight, 6);
        Assert.True(ConvergenceMeter.IdentifierWeight > ConvergenceMeter.IntentWeight);
    }

    /// <summary>The penalty for contradicting has a ceiling, so a score never runs away.</summary>
    [Fact]
    public void ThePenaltyIsCapped()
    {
        Assert.True(ConvergenceMeter.MaximumPenalty >= ConvergenceMeter.ContradictionPenalty);

        var measured = ConvergenceMeter.Measure(
            "Add stacking to InventorySlot. Add pooling to ProjectileSpawner. Add reloading to WeaponSlot. Add saving to ItemStack.",
            "Remove stacking from InventorySlot. Remove pooling from ProjectileSpawner. Remove reloading from WeaponSlot. Remove saving from ItemStack.",
            Project);

        Assert.NotNull(measured.Score);
        Assert.InRange(measured.Score!.Value, 0, 100);
    }

    /// <summary>A score is always a percentage, whatever went in.</summary>
    [Theory]
    [InlineData("Add stacking to InventorySlot and HealthBar.", "Remove everything from InventorySlot, HealthBar and ItemStack.")]
    [InlineData("InventorySlot InventorySlot InventorySlot", "InventorySlot")]
    [InlineData("Add Add Add stacking", "Add stacking")]
    public void AScoreIsAlwaysAPercentage(string first, string second)
    {
        var measured = ConvergenceMeter.Measure(first, second, Project);

        if (measured.Score is { } value)
        {
            Assert.InRange(value, 0, 100);
        }
    }

    /// <summary>
    /// Two positions with almost nothing named in common are not scored at all.
    /// </summary>
    /// <remarks>
    /// The finding this exists for, taken from a real debate. Six rounds of roughly five hundred
    /// words each about whether to store inventory as ScriptableObjects or as JSON, and the whole
    /// of every round scored on the one identifier both sides happened to use. One out of one is a
    /// hundred percent, which carried seventy percent of the weight, which is how a round where the
    /// two reached opposite conclusions came out at seventy percent converged.
    /// </remarks>
    [Fact]
    public void OneSharedIdentifierIsNotAMeasurement()
    {
        var measured = ConvergenceMeter.Measure(
            "ScriptableObjects give us editor authoring, validation in the inspector, and a workflow "
            + "the designers already understand, which matters more than the loading cost.",
            "ScriptableObjects are awkward to merge and hard to diff, so the loading cost is worth "
            + "paying for something a person can read and edit outside the editor.",
            Project);

        Assert.False(measured.IsMeasured);
        Assert.Null(measured.Score);
        Assert.Equal("not measurable", measured.Text);
        Assert.False(string.IsNullOrWhiteSpace(measured.Reason));
    }

    /// <summary>
    /// Not measurable is not nought, and nothing may read it as disagreement.
    /// </summary>
    /// <remarks>
    /// A thing that could not be determined is not a thing that failed. The gate that stops a
    /// debate is a measured score at or above the threshold, so an unmeasured round cannot settle
    /// one and cannot fail one either: it falls through to the round cap and the clock.
    /// </remarks>
    [Fact]
    public void NotMeasurableIsNotZero()
    {
        var measured = ConvergenceMeter.Measure(
            "ScriptableObjects are the better answer here for authoring reasons.",
            "ScriptableObjects are the wrong answer here for merging reasons.", Project);

        Assert.False(measured.IsMeasured);
        Assert.Null(measured.Score);

        // And it says as much where a person will read it.
        Assert.Contains("not measurable", measured.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a low score", measured.Breakdown(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Enough named between them, and it scores as it always did.</summary>
    [Fact]
    public void EnoughSharedMaterialStillScores()
    {
        var measured = ConvergenceMeter.Measure(
            "Put stacking on InventorySlot, keep HealthBar, and leave ItemStack alone.",
            "Put stacking on InventorySlot, keep HealthBar, and leave ItemStack alone.", Project);

        Assert.True(measured.IsMeasured);
        Assert.Equal(100, measured.Score);
        Assert.Equal(string.Empty, measured.Reason);
    }

    /// <summary>The threshold is three, and it is stated where the score is explained.</summary>
    [Fact]
    public void TheThresholdIsStated()
    {
        Assert.Equal(3, ConvergenceMeter.MinimumDistinctIdentifiers);
        Assert.Contains("3 distinct identifiers", ConvergenceMeter.WeightingSummary, StringComparison.Ordinal);
    }

    /// <summary>
    /// One identifier written two ways is one identifier.
    /// </summary>
    /// <remarks>
    /// Round one of the same debate counted ScriptableObject and ScriptableObjects separately,
    /// which moved the score on its own before anything else went wrong.
    /// </remarks>
    [Fact]
    public void SingularAndPluralAreTheSameIdentifier()
    {
        var measured = ConvergenceMeter.Measure(
            "Use ScriptableObject for items, keep HealthBar, and leave ItemStack alone.",
            "Use ScriptableObjects for items, keep HealthBar, and leave ItemStack alone.", Project);

        Assert.True(measured.IsMeasured);
        Assert.Equal(100, measured.Score);

        // One entry, not two, and neither side has one the other lacks.
        Assert.Contains("ScriptableObject", measured.SharedIdentifiers);
        Assert.Empty(measured.FirstOnlyIdentifiers);
        Assert.Empty(measured.SecondOnlyIdentifiers);
    }

    /// <summary>A word whose ending merely looks plural is left alone.</summary>
    /// <remarks>
    /// The normalisation is deliberately timid, because merging two identifiers that are genuinely
    /// different is worse than missing that two are the same.
    /// </remarks>
    [Theory]
    [InlineData("ItemClass")]
    [InlineData("GameStatus")]
    public void AWordThatMerelyEndsInSIsNotAPlural(string identifier)
    {
        var measured = ConvergenceMeter.Measure(
            $"Keep {identifier}, keep HealthBar, and leave ItemStack alone.",
            $"Keep {identifier}, keep HealthBar, and leave ItemStack alone.", Project);

        Assert.Contains(identifier, measured.SharedIdentifiers);
    }
}
