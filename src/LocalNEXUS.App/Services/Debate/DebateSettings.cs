namespace LocalNEXUS.App.Services.Debate;

/// <summary>What a model is being asked to do in a debate.</summary>
/// <remarks>
/// Two of these are adversarial by construction and one is not, which is why the pairing rules
/// exist. A debate needs disagreement to be productive, and two models given the same adversarial
/// hat cannot produce any: two defenders never disagree and two critics never propose anything.
/// </remarks>
public enum DebateRole
{
    /// <summary>Argue the position it believes is right, and change its mind if the other is better.</summary>
    Debate,

    /// <summary>Argue for the proposal and answer what is said against it.</summary>
    Defend,

    /// <summary>Attack the proposal and find where it breaks.</summary>
    Criticize
}

/// <summary>What a model is arguing from.</summary>
/// <remarks>
/// The pair that matters is one model arguing from the project and one from what is generally
/// right. That is the real tension in most decisions about a codebase, and it needs the project
/// index, which is the thing this application has that a chat window does not.
/// </remarks>
public enum DebateSource
{
    /// <summary>What the model knows, without being shown the project.</summary>
    OwnReasoning,

    /// <summary>The patterns already in the open project, read from the index.</summary>
    Codebase
}

/// <summary>Which of the two models wears the arbiter's hat.</summary>
/// <remarks>
/// A debate has exactly two model pins, so the outside read on how far apart the two positions are
/// has to come from one of the debaters wearing a different hat. That is weaker than a genuine
/// third model and it is said out loud rather than presented as impartial. What it still catches
/// is the failure it exists for: both models reporting near total agreement while a read of what
/// they actually wrote says otherwise.
/// </remarks>
public enum DebateArbiter
{
    /// <summary>The model on the first pin.</summary>
    First,

    /// <summary>The model on the second pin.</summary>
    Second
}

/// <summary>What happens when the rounds run out before the positions come together.</summary>
public enum NonConvergence
{
    /// <summary>
    /// A judge decides and the run carries on.
    /// </summary>
    /// <remarks>
    /// The default, because a run that stops to ask a question is worth nothing to somebody who
    /// walked away from it. Whether that is right depends on whether anybody is watching, which is
    /// exactly the kind of thing that belongs on the node rather than being decided here.
    /// </remarks>
    FallBackToJudge,

    /// <summary>The run pauses and asks what to do.</summary>
    AlertAndWait
}

/// <summary>How a judge resolves two positions.</summary>
public enum JudgeMode
{
    /// <summary>Read both, then write its own, informed by both and bound to neither.</summary>
    DecideIndependently,

    /// <summary>Pick the better position and emit it.</summary>
    ChooseASide,

    /// <summary>Merge the two into one.</summary>
    Combine
}
