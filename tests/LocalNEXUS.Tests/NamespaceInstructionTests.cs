using LocalNEXUS.App.Services.Editing;
using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Whether the coder is ever told to move the namespace.
/// </summary>
/// <remarks>
/// The evaluation task asking for a type to be moved between namespaces has never once passed, and
/// there were two candidate explanations that call for opposite responses. Either the instruction
/// is mangled somewhere between the planner and the coder, which is a defect here, or it arrives
/// intact and is ignored, which is the model and is not fixable from this side.
///
/// These follow the instruction along the whole path using the planner reply captured from a run,
/// so the answer rests on the bytes rather than on reading the code and believing it.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class NamespaceInstructionTests
{
    /// <summary>The planner reply from the run, verbatim.</summary>
    private const string PlannerReply =
        "DECISIONS\n"
        + "Assets/Scripts/InventorySlot.cs | EDIT | Game.Inventory.InventorySlot | "
        + "the InventorySlot class needs to be moved to the Game.Inventory namespace\n"
        + "\n"
        + "PLAN\n"
        + "1 | EDIT | Assets/Scripts/InventorySlot.cs | InventorySlot | "
        + "move the InventorySlot class into the Game.Inventory namespace";

    /// <summary>The file as the project holds it, which is what the coder is shown.</summary>
    private const string ExistingContent = """
        namespace Game
        {
            public class InventorySlot
            {
                public string ItemId;
                public int Count;
            }
        }
        """;

    private const string Instruction = "move the InventorySlot class into the Game.Inventory namespace";

    /// <summary>The plan row carries the instruction and the right file.</summary>
    /// <remarks>
    /// The four column decision row above it is the shape that used to lose a reply entirely. It
    /// does not matter here, because this reply also wrote a plan section, and the plan row is the
    /// one that becomes work.
    /// </remarks>
    [Fact]
    public void ThePlanRowCarriesTheInstruction()
    {
        var row = Assert.Single(PlanParser.Parse(PlannerReply).Rows);

        Assert.Equal(FileOperation.Edit, row.Operation);
        Assert.Equal("Assets/Scripts/InventorySlot.cs", row.RelativePath);
        Assert.Equal("InventorySlot", row.TypeName);
        Assert.Equal(Instruction, row.Intent);
    }

    /// <summary>
    /// The coder is asked for the whole file, not a patch.
    /// </summary>
    /// <remarks>
    /// Worth pinning, because a coder asked for a patch and replying with a whole file would have
    /// its answer applied to nothing, and the file would come out unchanged for a reason that has
    /// nothing to do with the model understanding the request.
    /// </remarks>
    [Fact]
    public void TheCoderIsAskedForTheWholeFile()
    {
        Assert.True(CodeEditApplier.WantsWholeFile(EditFormat.Automatic, false, ExistingContent.Length));
    }

    /// <summary>The message the coder is sent names the namespace it is being asked for.</summary>
    [Fact]
    public void TheCoderMessageNamesTheNamespace()
    {
        var row = Assert.Single(PlanParser.Parse(PlannerReply).Rows);

        var task = new CodeTask(
            1,
            row.RelativePath,
            row.TypeName,
            row.Operation,
            row.Intent,
            string.Empty,
            ExistingContent,
            "Game.InventorySlot",
            row.RelativePath);

        var message = PlanPrompt.BuildCoderMessage(task, string.Empty, wholeFile: true);

        Assert.Contains(Instruction, message, StringComparison.Ordinal);
        Assert.Contains("Game.Inventory", message, StringComparison.Ordinal);

        // And it is shown the file it is being asked to change, so it has both halves.
        Assert.Contains("public class InventorySlot", message, StringComparison.Ordinal);
        Assert.Contains("Return the complete file", message, StringComparison.Ordinal);
    }
}
