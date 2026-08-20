namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// A node that can be asked a question and will answer it with a model.
/// </summary>
/// <remarks>
/// The same trick as <see cref="ICodeRepairSource"/>, in the other direction. A node that needs a
/// model but is not itself a model node looks along its own wires for something that advertises
/// this, and asks. It never names a node type, the executor is not involved, and a graph wired
/// with nothing that can answer gets a clear refusal rather than a surprise.
///
/// The system prompt is supplied by the caller rather than taken from the node, because the node
/// is configured for its own job. A coder told to emit raw C# is the wrong voice for a planner,
/// and borrowing the endpoint without borrowing the instructions is the point.
/// </remarks>
public interface IPlanningModel
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
