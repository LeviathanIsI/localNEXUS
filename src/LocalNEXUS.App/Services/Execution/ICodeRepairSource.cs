namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// A node that can be asked to have another go at code it produced.
/// </summary>
/// <remarks>
/// This is what keeps the repair loop from being a special case. The node that checks code walks
/// its own incoming wire, asks whether whatever it finds there can revise, and drives the loop
/// through this interface without ever naming a concrete node type. A transform node upstream
/// simply cannot revise, and the check says so rather than failing in a confusing way.
///
/// The executor is not involved and does not need to be. It still orders nodes and hands each
/// one its inputs, exactly as it did before; what a node does while it is executing has never
/// been its business, and a node asking its own upstream neighbour for a second answer is no
/// different in kind from a node calling a model in the first place.
/// </remarks>
public interface ICodeRepairSource
{
    /// <summary>
    /// Whether this node is in a position to revise right now, and why not when it is not.
    /// </summary>
    /// <remarks>
    /// Asked before the first attempt so that a loop which cannot possibly work says so instead
    /// of spending three model calls finding out.
    /// </remarks>
    /// <param name="ctx">A context belonging to this node, so it can inspect its own wiring.</param>
    /// <param name="reason">Set to a human readable explanation when this returns false.</param>
    bool CanRepair(NodeExecutionContext ctx, out string reason);

    /// <summary>
    /// Produces a corrected version of the code.
    /// </summary>
    /// <param name="request">What was wrong with the last attempt.</param>
    /// <param name="ctx">A context belonging to this node, so it can read its own inputs.</param>
    /// <param name="ct">Cancels the attempt.</param>
    /// <returns>The revised code. Never null; an empty reply is an error for the caller to raise.</returns>
    Task<string> ReviseAsync(CodeRepairRequest request, NodeExecutionContext ctx, CancellationToken ct);
}
