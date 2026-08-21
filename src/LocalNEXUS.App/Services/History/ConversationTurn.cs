namespace LocalNEXUS.App.Services.History;

/// <summary>Who said one thing in the conversation.</summary>
public enum TurnRole
{
    /// <summary>The person driving.</summary>
    User,

    /// <summary>The graph, reporting what it did.</summary>
    Graph,

    /// <summary>The graph, asking something it could not resolve on its own.</summary>
    Question
}

/// <summary>
/// One thing said in the conversation for a project.
/// </summary>
/// <remarks>
/// Turns live in the same database as the runs, not in a store of their own. A turn that started
/// a run carries that run's identity, which is what lets the transcript and the record be two
/// views of one thing rather than two copies that can disagree.
/// </remarks>
/// <param name="Id">This turn's identity.</param>
/// <param name="ThreadId">Which conversation it belongs to. Starting fresh mints a new one.</param>
/// <param name="Role">Who said it.</param>
/// <param name="Text">What was said.</param>
/// <param name="At">When.</param>
/// <param name="RunId">The run this turn started, when it started one.</param>
public sealed record ConversationTurn(
    string Id,
    string ThreadId,
    TurnRole Role,
    string Text,
    DateTimeOffset At,
    string? RunId)
{
    /// <summary>True when the person said this.</summary>
    public bool IsUser => Role == TurnRole.User;

    /// <summary>True when this is the graph asking rather than reporting.</summary>
    public bool IsQuestion => Role == TurnRole.Question;

    /// <summary>The label the transcript puts above it.</summary>
    public string Speaker => Role switch
    {
        TurnRole.User => "You",
        TurnRole.Question => "Needs an answer",
        _ => "Graph"
    };

    /// <summary>The time on its own, for the transcript.</summary>
    public string Time => At.ToString("HH:mm");

    /// <summary>The turn as it goes into a prompt.</summary>
    public string ForPrompt => $"{(IsUser ? "User" : "Assistant")}: {Text}";
}

/// <summary>
/// One thing the planner could not decide for itself.
/// </summary>
/// <remarks>
/// Options are not decoration. A question without at least two named alternatives is not a
/// question somebody can answer quickly, and it is usually a sign the model wanted reassurance
/// rather than information. Those are dropped rather than asked.
/// </remarks>
/// <param name="Text">What is being asked.</param>
/// <param name="Options">The alternatives, named, drawn from what the project actually contains.</param>
public sealed record ClarificationQuestion(string Text, IReadOnlyList<string> Options)
{
    /// <summary>True when this is specific enough to be worth somebody's time.</summary>
    public bool IsAnswerable => !string.IsNullOrWhiteSpace(Text) && Options.Count >= 2;

    /// <summary>The question as the chat shows it and as it goes back into the prompt.</summary>
    public override string ToString()
        => Options.Count == 0 ? Text : $"{Text}{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", Options)}";
}

/// <summary>What came back from asking, or what was assumed when nothing did.</summary>
/// <param name="Answered">True when somebody replied.</param>
/// <param name="Text">What they said, or an empty string.</param>
public sealed record ClarificationOutcome(bool Answered, string Text)
{
    /// <summary>Nobody answered.</summary>
    public static ClarificationOutcome Unanswered { get; } = new(false, string.Empty);
}
