namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// A configured model, handed along a Model pin to a node that needs one to think with.
/// </summary>
/// <remarks>
/// What travels on a Model pin. It used to be found by searching the wires downstream for a node
/// that advertised itself, which worked and was invisible: nothing on the canvas said which model
/// did the planning, because the answer was whichever one happened to be wired after. Now the
/// wire says it, and one model can be handed to several consumers.
///
/// Still an interface rather than the node type, for the same reason it was one before. A node
/// that needs a model has no business knowing what a Model node is, and an extension contributing
/// a node can declare a Model input without referencing anything in this assembly's node list.
///
/// The system prompt is supplied by the caller rather than taken from the node, because the node
/// is configured for its own job. A coder told to emit raw C# is the wrong voice for a planner,
/// and borrowing the endpoint without borrowing the instructions is the point.
/// </remarks>
public interface IModelHandle
{
    /// <summary>Whether this node could answer right now, and why not when it could not.</summary>
    /// <param name="reason">Set to a human readable explanation when this returns false.</param>
    bool CanAnswer(out string reason);

    /// <summary>
    /// Sends one question and returns the whole reply.
    /// </summary>
    /// <param name="systemPrompt">The voice to answer in, supplied by the caller.</param>
    /// <param name="message">The question.</param>
    /// <param name="ctx">A context belonging to this node.</param>
    /// <param name="ct">Cancels the request.</param>
    Task<string> AnswerAsync(string systemPrompt, string message, NodeExecutionContext ctx, CancellationToken ct);
}
