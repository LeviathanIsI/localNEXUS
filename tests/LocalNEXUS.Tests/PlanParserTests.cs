using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Replies that answered the first section and stopped.
/// </summary>
/// <remarks>
/// Every string here is a planner reply captured verbatim from an evaluation run, not a reply
/// invented to look like one. The whole class of failure is a reply that wrote decisions and no
/// plan, and it is worth being precise about how little it takes: in the commonest shape the
/// planner said the right thing about the right file and simply never wrote the second heading.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class PlanParserTests
{
    /// <summary>A reply with both sections is read as it always was.</summary>
    [Fact]
    public void BothSectionsAreReadAsBefore()
    {
        var parsed = PlanParser.Parse(
            "DECISIONS\n"
            + "Assets/Scripts/Spinner.cs | EDIT | the class name needs to be changed to Rotator\n"
            + "\n"
            + "PLAN\n"
            + "1 | EDIT | Assets/Scripts/Spinner.cs | Rotator | rename the class to Rotator");

        var row = Assert.Single(parsed.Rows);

        Assert.Equal(FileOperation.Edit, row.Operation);
        Assert.Equal("Assets/Scripts/Spinner.cs", row.RelativePath);
        Assert.Equal("Rotator", row.TypeName);
        Assert.Equal("rename the class to Rotator", row.Intent);

        Assert.Single(parsed.Verdicts);
    }

    /// <summary>Decisions and no plan: the edits become the plan.</summary>
    [Fact]
    public void DecisionsAloneBecomeThePlan()
    {
        var parsed = PlanParser.Parse(
            "DECISIONS\nAssets/Scripts/Health.cs | EDIT | the current health value lives on this type");

        var row = Assert.Single(parsed.Rows);

        Assert.Equal(FileOperation.Edit, row.Operation);
        Assert.Equal("Assets/Scripts/Health.cs", row.RelativePath);
        Assert.Equal("Health", row.TypeName);
        Assert.Equal("the current health value lives on this type", row.Intent);
    }

    /// <summary>
    /// A plan row folded into a decision row still yields the file and the intent.
    /// </summary>
    /// <remarks>
    /// Five columns where the format asks for three, with the new path and the type name wedged
    /// into the middle. The prose is at the end, which is why the reason is read from the last
    /// column: the third holds a path here and would become what the coder is told the file is for.
    /// </remarks>
    [Fact]
    public void AMergedRowStillYieldsAPlan()
    {
        var parsed = PlanParser.Parse(
            "DECISIONS\n"
            + "Assets/Scripts/Spinner.cs | EDIT | Assets/Scripts/Rotator.cs | Rotator | rename the class to Rotator");

        var row = Assert.Single(parsed.Rows);

        Assert.Equal("Assets/Scripts/Spinner.cs", row.RelativePath);
        Assert.Equal("rename the class to Rotator", row.Intent);
    }

    /// <summary>The four column variant, which is where the namespace request ended up.</summary>
    [Fact]
    public void AFourColumnMergedRowStillYieldsAPlan()
    {
        var parsed = PlanParser.Parse(
            "DECISIONS\n"
            + "Assets/Scripts/InventorySlot.cs | EDIT | Game.Inventory.InventorySlot | "
            + "move the InventorySlot class into a Game.Inventory namespace");

        var row = Assert.Single(parsed.Rows);

        Assert.Equal("Assets/Scripts/InventorySlot.cs", row.RelativePath);
        Assert.Equal("move the InventorySlot class into a Game.Inventory namespace", row.Intent);
    }

    /// <summary>Use as is means leave the file alone, so it is not a plan row.</summary>
    [Fact]
    public void UseAsIsNeverBecomesAPlanRow()
    {
        var parsed = PlanParser.Parse(
            "DECISIONS\nAssets/Scripts/MathUtil.cs | USE_AS_IS | it already clamps correctly");

        Assert.Empty(parsed.Rows);
        Assert.Single(parsed.Verdicts);
    }

    /// <summary>So does ignore.</summary>
    [Fact]
    public void IgnoreNeverBecomesAPlanRow()
    {
        var parsed = PlanParser.Parse(
            "DECISIONS\nAssets/Scripts/WeaponData.cs | IGNORE | nothing here relates to the request");

        Assert.Empty(parsed.Rows);
    }

    /// <summary>
    /// Create new referencing names the file to tie into, not the file to write, so it is not one either.
    /// </summary>
    /// <remarks>
    /// The tempting one. Its path column looks exactly like a path to write to, and taking it would
    /// mean editing a file the planner asked to be left intact.
    /// </remarks>
    [Fact]
    public void CreateNewReferencingNeverBecomesAPlanRow()
    {
        var parsed = PlanParser.Parse(
            "DECISIONS\n"
            + "Assets/Scripts/Combat/CombatLog.cs | CREATE_NEW_REFERENCING CombatLog | records the last ten damage events");

        Assert.Empty(parsed.Rows);

        var verdict = Assert.Single(parsed.Verdicts);
        Assert.Equal(CandidateDecision.CreateNewReferencing, verdict.Decision);
    }

    /// <summary>
    /// A reply that wrote both sections is not planned twice.
    /// </summary>
    /// <remarks>
    /// The derivation is a fallback. Without this, the ordinary reply above would plan Spinner.cs
    /// once from its plan row and again from its decision row.
    /// </remarks>
    [Fact]
    public void APlanThatParsedIsNotSupplemented()
    {
        var parsed = PlanParser.Parse(
            "DECISIONS\n"
            + "Assets/Scripts/Enemy.cs | EDIT | the enemy takes the damage\n"
            + "Assets/Scripts/Health.cs | EDIT | the health type applies it\n"
            + "\n"
            + "PLAN\n"
            + "1 | EDIT | Assets/Scripts/Enemy.cs | Enemy | take a damage type");

        Assert.Single(parsed.Rows);
    }

    /// <summary>Several edits keep the order they were written in.</summary>
    [Fact]
    public void DerivedRowsKeepTheOrderTheyWereWrittenIn()
    {
        var parsed = PlanParser.Parse(
            "DECISIONS\n"
            + "Assets/Scripts/Enemy.cs | EDIT | the enemy takes the damage\n"
            + "Assets/Scripts/WeaponData.cs | IGNORE | not relevant\n"
            + "Assets/Scripts/Health.cs | EDIT | the health type applies it");

        Assert.Equal(
            new[] { "Assets/Scripts/Enemy.cs", "Assets/Scripts/Health.cs" },
            parsed.Rows.Select(r => r.RelativePath).ToArray());
    }

    /// <summary>A decision on something that is not a C# file is not a file to write.</summary>
    [Fact]
    public void ADecisionOnSomethingThatIsNotASourceFileIsNotAPlanRow()
    {
        var parsed = PlanParser.Parse(
            "DECISIONS\nAssets/Prefabs/Player.prefab | EDIT | the player needs the new component");

        Assert.Empty(parsed.Rows);
    }

    /// <summary>A reply with nothing readable in it is still an empty plan.</summary>
    /// <remarks>
    /// Captured from a run where the planner answered the clarification format in prose. A parser
    /// that found a plan in this would be worse than one that reports it could not.
    /// </remarks>
    [Fact]
    public void AReplyWithNoRowsIsStillEmpty()
    {
        var parsed = PlanParser.Parse(
            "QUESTIONS\nWhich of the following should be optimized for speed:  \n- Enemy.cs  \n- Health.cs");

        Assert.Empty(parsed.Rows);
        Assert.Empty(parsed.Verdicts);
    }

    /// <summary>An empty reply is an empty plan.</summary>
    [Fact]
    public void AnEmptyReplyIsAnEmptyPlan()
    {
        Assert.Empty(PlanParser.Parse(string.Empty).Rows);
        Assert.Empty(PlanParser.Parse(null).Rows);
    }
}
