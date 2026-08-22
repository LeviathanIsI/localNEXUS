using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// A node that exists only in the test assembly, records that it ran, and passes text through.
/// </summary>
/// <remarks>
/// This is the point of the executor tests. The executor is supposed to know nothing about node
/// types, and the only way to hold it to that is to run it over a type it could not possibly know
/// about, defined outside the application entirely and absent from <c>NodeFactory</c>. If the
/// executor ever grows a special case, a node it has never heard of is what finds it.
/// </remarks>
public sealed class RecordingNode : NodeBase
{
    private readonly List<string> _log;

    public RecordingNode(string title, List<string> log)
        : this(title, log, PinType.Text)
    {
    }

    /// <summary>A node whose pins carry a given type, so it can be wired to anything.</summary>
    public RecordingNode(string title, List<string> log, PinType pinType)
        : base(title)
    {
        _log = log;
        In = AddInput("In", pinType);
        Out = AddOutput("Out", pinType);
    }

    public override string TypeKey => "TestRecording";

    /// <summary>What this node was handed.</summary>
    public Pin In { get; }

    /// <summary>What it passes on.</summary>
    public Pin Out { get; }

    /// <summary>Set to have the node throw instead of completing.</summary>
    public string? FailWith { get; set; }

    /// <summary>Appended to whatever arrives, so a chain can be read back from its output.</summary>
    public string Append { get; set; } = string.Empty;

    /// <summary>What this node saw on its input the last time it ran.</summary>
    public string? Received { get; private set; }

    /// <summary>How many times it has run.</summary>
    public int Runs { get; private set; }

    public override Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        Runs++;
        Received = ctx.GetText(In);
        _log.Add(Title);

        if (FailWith is { } message)
        {
            throw new InvalidOperationException(message);
        }

        return Task.FromResult(NodeResult.FromPin(Out, Received + Append));
    }

    public override JsonObject SaveSettings() => new() { ["append"] = Append };

    public override void LoadSettings(JsonObject settings)
        => Append = settings["append"]?.GetValue<string>() ?? string.Empty;
}
