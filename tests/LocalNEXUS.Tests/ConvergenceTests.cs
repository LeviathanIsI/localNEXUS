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
/// same content in different prose has to score high, and different content in similar prose has
/// to score low. A meter that cannot tell those apart is measuring style.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ConvergenceTests
{
    [Fact]
    public void IdenticalPositionsScoreTop()
    {
        const string position = "Put stacking on InventorySlot. Health should implement IDamageable.";

        var measured = ConvergenceMeter.Measure(position, position);

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
            "I would add stacking to InventorySlot, and have Health implement IDamageable.",
            "Stacking belongs on InventorySlot. Health should implement IDamageable as well.");

        Assert.NotNull(measured.Score);
        Assert.True(measured.Score >= 60, $"scored {measured.Score}: {measured.Breakdown()}");
        Assert.Empty(measured.Contradictions);
    }

    /// <summary>Different proposals in similar prose score low, because the nouns differ.</summary>
    [Fact]
    public void DifferentProposalsInSimilarProseScoreLow()
    {
        var measured = ConvergenceMeter.Measure(
            "I would add stacking to InventorySlot and have Health implement IDamageable.",
            "I would add pooling to ProjectileSpawner and have Weapon implement IReloadable.");

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
            "Add stacking to InventorySlot.",
            "Add stacking to InventorySlot.");

        var opposed = ConvergenceMeter.Measure(
            "Add stacking to InventorySlot.",
            "Remove stacking from InventorySlot.");

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
            "Add stacking to InventorySlot. Do not remove Health.",
            "Add stacking to InventorySlot. Do not remove Health.");

        Assert.Empty(measured.Contradictions);
        Assert.Equal(100, measured.Score);
    }

    /// <summary>Two positions with nothing concrete in them cannot be measured, and say so.</summary>
    [Fact]
    public void NothingConcreteIsNotMeasurable()
    {
        var measured = ConvergenceMeter.Measure("I agree.", "So do I.");

        Assert.Null(measured.Score);
        Assert.Equal("not measurable", measured.Text);
    }

    [Fact]
    public void AnEmptyPositionIsNotMeasurable()
        => Assert.Null(ConvergenceMeter.Measure(string.Empty, "something concrete about InventorySlot").Score);

    /// <summary>The score is symmetric, because neither side is the reference.</summary>
    [Fact]
    public void TheOrderOfTheTwoDoesNotMatter()
    {
        const string a = "Add stacking to InventorySlot and pooling to ProjectileSpawner.";
        const string b = "Add pooling to ProjectileSpawner. Leave InventorySlot alone.";

        Assert.Equal(ConvergenceMeter.Measure(a, b).Score, ConvergenceMeter.Measure(b, a).Score);
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
            "Add stacking to InventorySlot.",
            "Add pooling to ProjectileSpawner.");

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
            "Add stacking to InventorySlot. Add pooling to Spawner. Add reloading to Weapon. Add saving to Inventory.",
            "Remove stacking from InventorySlot. Remove pooling from Spawner. Remove reloading from Weapon. Remove saving from Inventory.");

        Assert.NotNull(measured.Score);
        Assert.InRange(measured.Score!.Value, 0, 100);
    }

    /// <summary>A score is always a percentage, whatever went in.</summary>
    [Theory]
    [InlineData("Add stacking to InventorySlot.", "Remove everything from InventorySlot and delete Health.")]
    [InlineData("InventorySlot InventorySlot InventorySlot", "InventorySlot")]
    [InlineData("Add Add Add stacking", "Add stacking")]
    public void AScoreIsAlwaysAPercentage(string first, string second)
    {
        var measured = ConvergenceMeter.Measure(first, second);

        if (measured.Score is { } value)
        {
            Assert.InRange(value, 0, 100);
        }
    }
}
