using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// A node that emits code and can be asked for another attempt at it.
/// </summary>
/// <remarks>
/// Stands where a Model node stands in a repair loop. It records every request it is handed, which
/// is where a repair loop actually goes wrong: looping the right number of times while never
/// sending the diagnostics is indistinguishable from working until you read the prompts.
///
/// It implements <see cref="ICodeRepairSource"/> and nothing else, which is the point being
/// proved. No node type is named anywhere in the loop, so a node the application has never heard
/// of can take part in one.
/// </remarks>
public sealed class RepairableNode : NodeBase, ICodeRepairSource
{
    private readonly Queue<string> _attempts = new();
    private readonly List<CodeRepairRequest> _requests = new();

    public RepairableNode(string title, string first)
        : base(title)
    {
        First = first;
        Out = AddOutput("Code", PinType.Code);
    }

    public override string TypeKey => "TestRepairable";

    /// <summary>What it emits when the run reaches it.</summary>
    public string First { get; }

    /// <summary>The pin a compiler check is wired to.</summary>
    public Pin Out { get; }

    /// <summary>Set to have it decline to repair, with this as the reason.</summary>
    public string? Refuse { get; set; }

    /// <summary>Every repair request handed to it, in order.</summary>
    public IReadOnlyList<CodeRepairRequest> Requests => _requests;

    /// <summary>Queues what each successive repair attempt returns.</summary>
    public RepairableNode Attempts(params string[] revisions)
    {
        foreach (var revision in revisions)
        {
            _attempts.Enqueue(revision);
        }

        return this;
    }

    public bool CanRepair(NodeExecutionContext ctx, out string reason)
    {
        reason = Refuse ?? string.Empty;
        return Refuse is null;
    }

    public Task<string> ReviseAsync(CodeRepairRequest request, NodeExecutionContext ctx, CancellationToken ct)
    {
        _requests.Add(request);

        return Task.FromResult(_attempts.Count > 0 ? _attempts.Dequeue() : request.FailingCode);
    }

    public override Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
        => Task.FromResult(NodeResult.FromPin(Out, First));

    public override JsonObject SaveSettings() => new();

    public override void LoadSettings(JsonObject settings)
    {
    }
}
