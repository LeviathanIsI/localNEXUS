using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Planning;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// A node that puts a ready made plan of generated files onto a wire.
/// </summary>
/// <remarks>
/// Stands where a coder stands, without a model. The compile check runs a plan differently from a
/// single file and the difference is the whole of what the accumulated set is for, so testing it
/// needs something that emits a list rather than a string.
/// </remarks>
public sealed class PlanEmittingNode : NodeBase
{
    private readonly IReadOnlyList<GeneratedFile> _plan;

    public PlanEmittingNode(string title, IReadOnlyList<GeneratedFile> plan)
        : base(title)
    {
        _plan = plan;
        Out = AddOutput("Code", PinType.Code);
    }

    public override string TypeKey => "TestPlanEmitter";

    /// <summary>The pin a compiler check is wired to.</summary>
    public Pin Out { get; }

    public override Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
        => Task.FromResult(NodeResult.FromPin(Out, _plan));

    public override JsonObject SaveSettings() => new();

    public override void LoadSettings(JsonObject settings)
    {
    }
}
