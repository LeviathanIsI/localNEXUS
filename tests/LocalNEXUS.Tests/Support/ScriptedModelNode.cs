using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// A node that sits on a Model pin and answers from a script.
/// </summary>
/// <remarks>
/// The application's own Model node needs an endpoint, a provider and, for a hosted one, a key,
/// none of which a test should be assembling. What anything consuming a model actually depends on
/// is <see cref="IModelHandle"/>, which is two methods, so a test can supply one and everything
/// downstream of it becomes deterministic.
///
/// That this works at all is the design holding: Debate, Triage and everything else on a Model pin
/// were written against the interface rather than against the node, so none of them can tell the
/// difference. No production code was changed to allow this.
/// </remarks>
public sealed class ScriptedModelNode : NodeBase, IModelHandle
{
    private readonly Queue<string> _replies = new();
    private readonly List<string> _asked = new();

    private string _whenEmpty = "no reply was scripted";

    public ScriptedModelNode(string title)
        : base(title)
    {
        Self = AddOutput("Model", PinType.Model);

        // Appended after Self, never before it, because a saved graph matches pins by name and
        // falls back to position. Present so a test can wire this node as something other than a
        // reference, which is the difference between a node that is only configuration and a node
        // that is also a step.
        Reply = AddOutput("Reply", PinType.Text);
    }

    public override string TypeKey => "TestScriptedModel";

    /// <summary>The pin a consumer wires itself to.</summary>
    public Pin Self { get; }

    /// <summary>An ordinary output, so this node can be wired as a step as well as a reference.</summary>
    public Pin Reply { get; }

    /// <summary>True once the run has executed this node as a step.</summary>
    public bool Executed { get; private set; }

    /// <summary>Set to have the handle report that it cannot answer.</summary>
    public string? Unavailable { get; set; }

    /// <summary>Every question this was asked, in order, as system prompt and message.</summary>
    public IReadOnlyList<string> Asked => _asked;

    /// <summary>How many times anything asked it something.</summary>
    public int AnswerCount => _asked.Count;

    /// <summary>Queues replies, handed out in order.</summary>
    public ScriptedModelNode Replies(params string[] texts)
    {
        foreach (var text in texts)
        {
            _replies.Enqueue(text);
        }

        return this;
    }

    /// <summary>What to answer once the script has run out.</summary>
    public ScriptedModelNode ThenAlways(string text)
    {
        _whenEmpty = text;
        return this;
    }

    public bool CanAnswer(out string reason)
    {
        reason = Unavailable ?? string.Empty;
        return Unavailable is null;
    }

    public Task<string> AnswerAsync(string systemPrompt, string message, NodeExecutionContext ctx, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _asked.Add(systemPrompt + "\n" + message);

        return Task.FromResult(_replies.Count > 0 ? _replies.Dequeue() : _whenEmpty);
    }

    /// <summary>Records that the run treated this as a step, and says something on its own pin.</summary>
    public override Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        Executed = true;
        return Task.FromResult(NodeResult.FromPin(Reply, "ran"));
    }

    public override JsonObject SaveSettings() => new();

    public override void LoadSettings(JsonObject settings)
    {
    }
}
