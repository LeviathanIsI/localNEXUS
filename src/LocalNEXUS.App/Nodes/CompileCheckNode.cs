using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Compilation;
using LocalNEXUS.App.Services.Execution;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Compiles the code arriving on its input, and asks whoever produced it to fix what does not.
/// </summary>
/// <remarks>
/// This is what makes a run's success mean something. Without it a model can produce plausible,
/// broken C#, the file is written, and the run reports that everything went well.
///
/// The repair loop lives here rather than in the executor, which orders nodes and knows nothing
/// about any of them. This node follows its own incoming wire, asks whatever it finds there
/// whether it implements <see cref="ICodeRepairSource"/>, and if it does, hands it the compiler
/// errors and asks for another go. It never names a node type, so a coder node is not special
/// cased and a node that cannot revise simply reports that it cannot.
///
/// A cycle in the graph would be the other way to express this, and it is not available: the
/// executor rejects cycles, and it is right to, because the retry cap belongs in a setting rather
/// than in how many times somebody drew a loop.
/// </remarks>
public sealed partial class CompileCheckNode : NodeBase
{
    /// <summary>Repair attempts allowed by default after the first failure.</summary>
    public const int DefaultRetryLimit = 3;

    /// <summary>The most attempts the panel will accept, so a typo cannot start a hundred model calls.</summary>
    public const int MaximumRetryLimit = 10;

    /// <summary>
    /// How many diagnostics are sent back to the coder. One missing brace can produce fifty knock
    /// on errors, and burying the real one under them makes the fix less likely, not more.
    /// </summary>
    private const int DiagnosticsSentToCoder = 20;

    /// <summary>How many diagnostics go into the feed and the failure message.</summary>
    private const int DiagnosticsShown = 12;

    /// <summary>The name used for diagnostics when the code declares no type to take one from.</summary>
    private const string FallbackFileName = "Generated.cs";

    /// <summary>Repair attempts allowed after the first failed compile.</summary>
    [ObservableProperty]
    private int _retryLimit = DefaultRetryLimit;

    /// <summary>What happens to the run when the code still does not compile.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FaultsTheRun))]
    private CompileFailureBehaviour _failureBehaviour = CompileFailureBehaviour.FaultTheRun;

    /// <summary>How the last check ended. Drives the badge in the settings panel.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutcomeText))]
    private CompileOutcome _outcome = CompileOutcome.NotRun;

    /// <summary>The compiler diagnostics from the last check, as a compiler prints them.</summary>
    [ObservableProperty]
    private string _lastDiagnostics = string.Empty;

    /// <summary>What the last check compiled against, so a passing result can be judged.</summary>
    [ObservableProperty]
    private string _referenceSummary = string.Empty;

    public CompileCheckNode()
        : base("Compile Check")
    {
        Code = AddInput("Code", PinType.Code);
        Checked = AddOutput("Code", PinType.Code);
    }

    /// <summary>Receives the code to check.</summary>
    public Pin Code { get; }

    /// <summary>Carries onward the code that compiled, or the last attempt when failure is tolerated.</summary>
    public Pin Checked { get; }

    /// <inheritdoc />
    public override string TypeKey => "CompileCheck";

    /// <summary>True when a failed check stops the run. Bound by the settings panel.</summary>
    public bool FaultsTheRun => FailureBehaviour == CompileFailureBehaviour.FaultTheRun;

    /// <summary>One line describing how the last check ended.</summary>
    public string OutcomeText => Outcome switch
    {
        CompileOutcome.Checking => "Checking",
        CompileOutcome.Compiled => "Compiled",
        CompileOutcome.Repaired => "Repaired, then compiled",
        CompileOutcome.Failed => "Did not compile",
        CompileOutcome.Unavailable => "Could not be checked",
        _ => "Not run yet"
    };

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var source = ctx.GetText(Code);

        if (string.IsNullOrWhiteSpace(source))
        {
            throw new InvalidOperationException(
                $"{Title} received nothing to check. Connect a node to its Code pin.");
        }

        Outcome = CompileOutcome.Checking;
        LastDiagnostics = string.Empty;

        var compiler = ctx.Services.Compiler;
        var projectPath = ctx.Services.UnityProject.ProjectPath;
        var fileName = RoslynUnityCompiler.DeriveFileName(source, FallbackFileName);

        CompileResult result;
        try
        {
            result = await compiler.CompileAsync(source, fileName, projectPath, ct).ConfigureAwait(false);
        }
        catch (CompilerUnavailableException ex)
        {
            return Unavailable(ctx, source, ex);
        }

        ReferenceSummary = result.ReferenceSummary;
        ReportAttempt(ctx, attempt: 0, fileName, result);

        if (result.Succeeded)
        {
            Outcome = CompileOutcome.Compiled;
            StatusMessage = $"{fileName} compiled in {result.Elapsed.TotalMilliseconds:0} ms";
            return NodeResult.FromPin(Checked, source);
        }

        var repaired = await TryRepairAsync(ctx, source, fileName, result, ct).ConfigureAwait(false);

        if (repaired.Result.Succeeded)
        {
            Outcome = CompileOutcome.Repaired;
            StatusMessage = $"{fileName} compiled after {repaired.Attempts} repair attempt(s)";
            return NodeResult.FromPin(Checked, repaired.Code);
        }

        return Fail(ctx, fileName, repaired, ct);
    }

    /// <inheritdoc />
    public override JsonObject SaveSettings() => new()
    {
        ["retryLimit"] = RetryLimit,
        ["failureBehaviour"] = FailureBehaviour.ToString()
    };

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        RetryLimit = Math.Clamp(settings["retryLimit"]?.GetValue<int>() ?? DefaultRetryLimit, 0, MaximumRetryLimit);

        FailureBehaviour = Enum.TryParse<CompileFailureBehaviour>(
            settings["failureBehaviour"]?.GetValue<string>(),
            out var behaviour)
            ? behaviour
            : CompileFailureBehaviour.FaultTheRun;
    }

    partial void OnRetryLimitChanged(int value)
    {
        var clamped = Math.Clamp(value, 0, MaximumRetryLimit);

        if (clamped != value)
        {
            RetryLimit = clamped;
        }
    }

    /// <summary>The outcome of a repair loop: the best code it reached, and what the compiler said about it.</summary>
    private readonly record struct RepairOutcome(string Code, CompileResult Result, int Attempts);

    /// <summary>
    /// Asks whoever produced the code to fix it, once per allowed attempt, stopping as soon as it
    /// compiles.
    /// </summary>
    private async Task<RepairOutcome> TryRepairAsync(
        NodeExecutionContext ctx,
        string source,
        string fileName,
        CompileResult firstFailure,
        CancellationToken ct)
    {
        var current = source;
        var result = firstFailure;

        if (RetryLimit == 0)
        {
            return new RepairOutcome(current, result, 0);
        }

        var upstream = ctx.GetSourceNode(Code);

        if (upstream is not ICodeRepairSource repairSource)
        {
            var what = upstream is null ? "nothing" : upstream.Title;

            ctx.Feed.Info(
                $"{Title}: nothing upstream can repair this",
                $"The code arrived from {what}, which cannot be asked for another attempt. Wire a model node into this node to enable repair.");

            return new RepairOutcome(current, result, 0);
        }

        var upstreamContext = ctx.ForNode(upstream);

        if (!repairSource.CanRepair(upstreamContext, out var reason))
        {
            ctx.Feed.Info($"{Title}: {upstream.Title} cannot repair this", reason);
            return new RepairOutcome(current, result, 0);
        }

        for (var attempt = 1; attempt <= RetryLimit; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var request = new CodeRepairRequest(
                attempt,
                RetryLimit,
                fileName,
                current,
                result.Diagnostics.OrderByDescending(d => d.Severity).ThenBy(d => d.Line).Take(DiagnosticsSentToCoder).ToList());

            ctx.Feed.Add(
                ActivityKind.NodeStarted,
                $"{Title}: repair attempt {attempt} of {RetryLimit}",
                $"Asking {upstream.Title} to fix {request.ErrorCount} error(s) in {fileName}",
                Id);

            StatusMessage = $"Repair attempt {attempt} of {RetryLimit}";

            var revised = await repairSource.ReviseAsync(request, upstreamContext, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(revised))
            {
                ctx.Feed.Info(
                    $"{Title}: repair attempt {attempt} produced nothing",
                    $"{upstream.Title} returned an empty reply, so the previous code stands.");

                continue;
            }

            current = revised;

            try
            {
                result = await ctx.Services.Compiler
                    .CompileAsync(current, fileName, ctx.Services.UnityProject.ProjectPath, ct)
                    .ConfigureAwait(false);
            }
            catch (CompilerUnavailableException)
            {
                // The project or the editor went away mid loop. The outer caller reports it.
                throw;
            }

            ReportAttempt(ctx, attempt, fileName, result);

            if (result.Succeeded)
            {
                return new RepairOutcome(current, result, attempt);
            }
        }

        return new RepairOutcome(current, result, RetryLimit);
    }

    /// <summary>Writes one attempt's outcome to the feed, so a loop is never silent.</summary>
    private void ReportAttempt(NodeExecutionContext ctx, int attempt, string fileName, CompileResult result)
    {
        var label = attempt == 0
            ? $"{Title}: {fileName}"
            : $"{Title}: {fileName} after repair attempt {attempt}";

        if (result.Succeeded)
        {
            ctx.Feed.Add(
                ActivityKind.NodeCompleted,
                $"{label} compiles",
                $"{result.Summary}. {result.ReferenceSummary}",
                Id);

            LastDiagnostics = string.Empty;
            return;
        }

        var listing = result.FormatDiagnostics(DiagnosticsShown);
        LastDiagnostics = listing;

        ctx.Feed.Add(
            ActivityKind.NodeFaulted,
            $"{label} does not compile",
            listing,
            Id);
    }

    /// <summary>
    /// Reports a check that could not be run. Not a compile failure, and deliberately not treated
    /// as one: the code passes through untouched and the run continues.
    /// </summary>
    private NodeResult Unavailable(NodeExecutionContext ctx, string source, CompilerUnavailableException ex)
    {
        Outcome = CompileOutcome.Unavailable;
        ReferenceSummary = ex.Message;
        StatusMessage = "Could not be checked";

        ctx.Feed.Info($"{Title}: nothing was checked", ex.Message);

        return NodeResult.FromPin(Checked, source);
    }

    /// <summary>Ends a check that ran and failed, in whichever way the node is configured to.</summary>
    private NodeResult Fail(NodeExecutionContext ctx, string fileName, RepairOutcome repaired, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        Outcome = CompileOutcome.Failed;

        var errors = repaired.Result.Errors.Count;
        var attempted = repaired.Attempts == 0
            ? "no repair was attempted"
            : $"{repaired.Attempts} repair attempt(s) did not fix it";

        var listing = repaired.Result.FormatDiagnostics(DiagnosticsShown);
        StatusMessage = $"{errors} error(s) remain in {fileName}";

        if (FailureBehaviour == CompileFailureBehaviour.FaultTheRun)
        {
            throw new InvalidOperationException(
                $"{Title}: {fileName} does not compile and {attempted}. "
                + $"{errors} error(s) remain:{Environment.NewLine}{listing}");
        }

        ctx.Feed.Info(
            $"{Title}: continuing with code that does not compile",
            $"{errors} error(s) remain in {fileName} and {attempted}. This node is set to continue rather than fault the run.");

        return NodeResult.FromPin(Checked, repaired.Code);
    }
}
