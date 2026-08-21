using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace LocalNEXUS.App.Nodes;

/// <summary>
/// Rewrites the value passing through it, either by filling a template or by evaluating a C#
/// expression.
/// </summary>
/// <remarks>
/// Two inputs: the code to change, and the rule describing the change. The rule input is optional,
/// and with nothing wired the rule typed on the node is used, which is what keeps the default path
/// free: stripping a markdown fence must not require a prompt and a model in front of it, because
/// the repair loop depends on it.
///
/// When something is wired to the rule pin, whatever arrives is the rule. That is the shape worth
/// having: a prompt describing a change in plain English, a model turning it into a pattern, and
/// this node applying it. The model authors the rule and the node executes it. Nothing here calls
/// a model, so a patch is fast and repeatable and the code never leaves the machine to be
/// reformatted.
///
/// Regex mode is the default and the one that has to work everywhere. Template mode covers
/// wrapping or lightly editing a value. Script mode exists for everything else and is compiled
/// through Roslyn; compilation is cached against the expression text, so a graph that runs the
/// same transform repeatedly pays for it once.
///
/// It passes repair requests through rather than answering them, because it did not write the
/// code and cannot fix it. Its own upstream is asked, and whatever comes back is put through this
/// transform on the way out. That matters for the ordinary pipeline, where this node is what
/// strips a markdown fence from a model reply: without the pass through, a repaired reply would
/// arrive at the compiler still wrapped in one and could never compile.
/// </remarks>
public sealed partial class PatchNode : NodeBase, ICodeRepairSource
{
    /// <summary>The placeholder replaced with the incoming value in template mode.</summary>
    public const string InputPlaceholder = "{{input}}";

    /// <summary>
    /// The starting pattern. Model replies often arrive wrapped in a markdown code fence even when
    /// the prompt asks otherwise, and a fenced reply is not a valid C# file, so the default unwraps
    /// one when it is present and leaves anything else untouched.
    /// </summary>
    /// <remarks>
    /// A pattern rather than a script, and that is the fix for a real failure. This same rule used
    /// to be a Roslyn expression, and the script compiler cannot be built inside a single file
    /// executable, so every published build shipped a Patch node that quietly did nothing and a
    /// repair loop that handed fenced replies to a compiler. The regular expression engine needs
    /// nothing but itself and is there in every build.
    ///
    /// The surrounding whitespace is absorbed by the pattern rather than trimmed afterwards, so a
    /// rule meant to preserve whitespace is never quietly overruled.
    /// </remarks>
    public const string DefaultRegexPattern =
        @"(?s)\A\s*```[A-Za-z0-9#+_-]*[ \t]*\r?\n(.*?)\r?\n?```\s*\z";

    /// <summary>What the default pattern puts back: the contents of the fence and nothing else.</summary>
    public const string DefaultRegexReplacement = "$1";

    /// <summary>
    /// How long a pattern may run before it is abandoned.
    /// </summary>
    /// <remarks>
    /// A rule can now be written by a model, and a model can write a pattern that backtracks for
    /// the rest of the afternoon on input it did not anticipate. A bounded failure naming the rule
    /// beats a run that never ends.
    /// </remarks>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The starting expression for script mode, kept for a graph that already chose it.
    /// </summary>
    public const string DefaultScriptExpression =
        "Regex.Replace(input.Trim(), @\"(?s)^```[A-Za-z0-9#+_-]*\\s*\\r?\\n(.*?)\\r?\\n?```$\", \"$1\").Trim()";

    /// <summary>
    /// Options for the script compiler, built on first use rather than in a static constructor.
    /// </summary>
    /// <remarks>
    /// Roslyn builds a reference from an assembly by reading the file it was loaded from, and in a
    /// single file publish there is no such file: the assemblies are inside the executable and
    /// report no location, so asking for them throws. Doing that in a static constructor made the
    /// whole type unusable, and because a binding to any property of a node runs its type
    /// initializer, adding a Transform node took the published application down with it.
    ///
    /// So it is built lazily and the failure is caught. Anything that still works keeps working:
    /// template mode does not compile anything, and a script node reports plainly that it could
    /// not build a compiler rather than crashing the window.
    /// </remarks>
    private static readonly Lazy<ScriptOptions?> ScriptCompilationOptions = new(() =>
    {
        var options = ScriptOptions.Default.WithImports(
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.Text",
            "System.Text.RegularExpressions");

        try
        {
            return options.WithReferences(
                typeof(object).Assembly,
                typeof(Enumerable).Assembly,
                typeof(Regex).Assembly);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    });

    /// <summary>
    /// True when a script transform can be compiled in this build.
    /// </summary>
    /// <remarks>
    /// Asked at startup and reported, because this is a capability that fails quietly: the default
    /// transform is the one that strips a markdown fence off a model reply, the repair loop depends
    /// on it, and a build where it cannot compile should say so rather than wait to be found out
    /// mid run.
    /// </remarks>
    public static bool CanCompileScripts => ScriptCompilationOptions.Value is not null;

    /// <summary>Which transform is applied.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRegexMode))]
    [NotifyPropertyChangedFor(nameof(IsTemplateMode))]
    [NotifyPropertyChangedFor(nameof(IsScriptMode))]
    private PatchMode _mode = PatchMode.Regex;

    /// <summary>The pattern matched in regex mode.</summary>
    [ObservableProperty]
    private string _regexPattern = DefaultRegexPattern;

    /// <summary>What a match is replaced with in regex mode.</summary>
    [ObservableProperty]
    private string _regexReplacement = DefaultRegexReplacement;

    /// <summary>The template applied in template mode. Occurrences of <c>{{input}}</c> are substituted.</summary>
    [ObservableProperty]
    private string _template = InputPlaceholder;

    /// <summary>The C# expression evaluated in script mode.</summary>
    [ObservableProperty]
    private string _scriptExpression = DefaultScriptExpression;

    private ScriptRunner<object>? _compiled;
    private string? _compiledFor;

    public PatchNode()
        : base("Patch")
    {
        Source = AddInput("Code", PinType.Code);
        Rule = AddInput("Rule", PinType.Text);
        Result = AddOutput("Code", PinType.Code);
    }

    /// <summary>Receives the value to rewrite.</summary>
    public Pin Source { get; }

    /// <summary>
    /// Receives the rule to apply. Optional: with nothing wired, the rule on the node is used.
    /// </summary>
    public Pin Rule { get; }

    /// <summary>Carries the rewritten value onwards.</summary>
    public Pin Result { get; }

    /// <summary>Literal substitutions applied after the template is filled.</summary>
    public ObservableCollection<FindReplacePair> Replacements { get; } = new();

    /// <inheritdoc />
    public override string TypeKey => "Patch";

    /// <summary>True when regex mode is selected. Drives which editor is shown.</summary>
    public bool IsRegexMode => Mode == PatchMode.Regex;

    /// <summary>True when template mode is selected. Drives which editor is shown.</summary>
    public bool IsTemplateMode => Mode == PatchMode.Template;

    /// <summary>True when script mode is selected.</summary>
    public bool IsScriptMode => Mode == PatchMode.Script;

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var input = ctx.GetText(Source);
        var rule = ResolveRule(ctx, out var wired);

        var output = await ApplyAsync(rule, input, ct).ConfigureAwait(false);

        StatusMessage = $"{rule.Kind}{(wired ? " from the rule pin" : string.Empty)}: "
                        + $"{input.Length} to {output.Length} characters";

        return NodeResult.FromPin(Result, output);
    }

    /// <inheritdoc />
    public override JsonObject SaveSettings()
    {
        var replacements = new JsonArray();
        foreach (var pair in Replacements)
        {
            replacements.Add(new JsonObject
            {
                ["find"] = pair.Find,
                ["replace"] = pair.Replace
            });
        }

        return new JsonObject
        {
            ["mode"] = Mode.ToString(),
            ["regexPattern"] = RegexPattern,
            ["regexReplacement"] = RegexReplacement,
            ["template"] = Template,
            ["scriptExpression"] = ScriptExpression,
            ["replacements"] = replacements
        };
    }

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        if (Enum.TryParse<PatchMode>(settings["mode"]?.GetValue<string>(), out var mode))
        {
            Mode = mode;
        }

        RegexPattern = settings["regexPattern"]?.GetValue<string>() ?? DefaultRegexPattern;
        RegexReplacement = settings["regexReplacement"]?.GetValue<string>() ?? DefaultRegexReplacement;
        Template = settings["template"]?.GetValue<string>() ?? InputPlaceholder;
        ScriptExpression = settings["scriptExpression"]?.GetValue<string>() ?? DefaultScriptExpression;

        Replacements.Clear();
        if (settings["replacements"] is JsonArray array)
        {
            foreach (var element in array.OfType<JsonObject>())
            {
                Replacements.Add(new FindReplacePair(
                    element["find"]?.GetValue<string>() ?? string.Empty,
                    element["replace"]?.GetValue<string>() ?? string.Empty));
            }
        }
    }

    /// <inheritdoc />
    public bool CanRepair(NodeExecutionContext ctx, out string reason)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        reason = string.Empty;

        var upstreamNode = ctx.GetSourceNode(Source);

        if (upstreamNode is not ICodeRepairSource upstream)
        {
            reason = $"{Title} passes repair requests upstream, and nothing that can revise is wired into it.";
            return false;
        }

        return upstream.CanRepair(ctx.ForNode(upstreamNode), out reason);
    }

    /// <inheritdoc />
    public async Task<string> ReviseAsync(CodeRepairRequest request, NodeExecutionContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ctx);

        var upstreamNode = ctx.GetSourceNode(Source);

        if (upstreamNode is not ICodeRepairSource upstream)
        {
            throw new InvalidOperationException(
                $"{Title} cannot produce a new attempt: nothing that can revise is wired into it.");
        }

        var revised = await upstream
            .ReviseAsync(request, ctx.ForNode(upstreamNode), ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(revised))
        {
            return string.Empty;
        }

        // The revised reply goes through this transform exactly as the first one did, so whatever
        // this node is for, unwrapping a fence or renaming a symbol, still applies to the fix.
        return await ApplyAsync(ResolveRule(ctx, out _), revised, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The rule in force: whatever arrived on the pin, or the one typed on the node.
    /// </summary>
    /// <param name="wired">True when the rule came from the pin rather than from the node.</param>
    /// <remarks>
    /// A pin with nothing connected reads as absent rather than as an empty rule, which is what
    /// keeps the default path working with no prompt and no model in front of it. A pin that is
    /// connected and produced nothing is a different thing and is refused, because that is a
    /// wiring mistake and silently patching nothing would hide it.
    /// </remarks>
    private PatchRule ResolveRule(NodeExecutionContext ctx, out bool wired)
    {
        wired = ctx.GetSourceNode(Rule) is not null;

        if (!wired)
        {
            return new PatchRule(Mode, Mode switch
            {
                PatchMode.Template => Template ?? string.Empty,
                PatchMode.Script => ScriptExpression ?? string.Empty,
                _ => RegexPattern ?? string.Empty
            }, RegexReplacement ?? string.Empty);
        }

        return PatchRule.Parse(ctx.GetText(Rule), Mode, RegexReplacement ?? string.Empty);
    }

    /// <summary>Applies a rule to a value, mechanically and without asking anything.</summary>
    private async Task<string> ApplyAsync(PatchRule rule, string input, CancellationToken ct)
        => rule.Kind switch
        {
            PatchMode.Template => ApplyTemplate(rule.Primary, input),
            PatchMode.Script => await RunScriptAsync(rule.Primary, input, ct).ConfigureAwait(false),
            _ => ApplyPattern(rule, input)
        };

    /// <summary>
    /// Matches the pattern and replaces what it finds.
    /// </summary>
    /// <remarks>
    /// A pattern that matches nothing is not a failure: that is a rule with nothing to say about
    /// this particular input, and the code passes through. A pattern that will not compile, or one
    /// that runs away, is a failure, because neither can be what anybody meant.
    /// </remarks>
    private string ApplyPattern(PatchRule rule, string input)
    {
        if (string.IsNullOrEmpty(rule.Primary))
        {
            throw new PatchRuleException($"{Title} has no pattern to apply. Type one, or wire a rule into it.");
        }

        try
        {
            return Regex.Replace(input, rule.Primary, rule.Replacement ?? string.Empty, RegexOptions.None, PatternTimeout);
        }
        catch (ArgumentException ex)
        {
            throw new PatchRuleException(
                $"{Title} could not read its pattern: {ex.Message}{Environment.NewLine}{rule.Primary}", ex);
        }
        catch (RegexMatchTimeoutException ex)
        {
            throw new PatchRuleException(
                $"{Title} gave up on its pattern after {PatternTimeout.TotalSeconds:0} seconds. "
                + $"It matches this input too slowly to use:{Environment.NewLine}{rule.Primary}", ex);
        }
    }

    /// <summary>Adds an empty substitution row, used by the settings panel.</summary>
    [RelayCommand]
    private void AddReplacement() => Replacements.Add(new FindReplacePair());

    /// <summary>Removes a substitution row.</summary>
    [RelayCommand]
    private void RemoveReplacement(FindReplacePair? pair)
    {
        if (pair is not null)
        {
            Replacements.Remove(pair);
        }
    }

    private string ApplyTemplate(string template, string input)
    {
        var output = (template ?? string.Empty).Replace(InputPlaceholder, input, StringComparison.Ordinal);

        foreach (var pair in Replacements)
        {
            if (!string.IsNullOrEmpty(pair.Find))
            {
                output = output.Replace(pair.Find, pair.Replace ?? string.Empty, StringComparison.Ordinal);
            }
        }

        return output;
    }

    private async Task<string> RunScriptAsync(string expression, string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return input;
        }

        var runner = GetOrCompileRunner(expression);

        object? value;
        try
        {
            value = await runner(new PatchScriptGlobals { input = input }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{Title} script failed at run time: {ex.Message}", ex);
        }

        return value?.ToString() ?? string.Empty;
    }

    private ScriptRunner<object> GetOrCompileRunner(string expression)
    {
        if (_compiled is not null && string.Equals(_compiledFor, expression, StringComparison.Ordinal))
        {
            return _compiled;
        }

        if (ScriptCompilationOptions.Value is not { } options)
        {
            throw new InvalidOperationException(
                $"{Title} cannot compile a script in this build: the script compiler needs the runtime assemblies as files, "
                + "and a single file executable keeps them inside itself. Use Find and replace instead, or run from a build "
                + "that is not published as a single file.");
        }

        try
        {
            var script = CSharpScript.Create<object>(expression, options, typeof(PatchScriptGlobals));
            _compiled = script.CreateDelegate();
            _compiledFor = expression;
            return _compiled;
        }
        catch (CompilationErrorException ex)
        {
            _compiled = null;
            _compiledFor = null;
            var diagnostics = string.Join("; ", ex.Diagnostics.Select(d => d.GetMessage()));
            throw new InvalidOperationException($"{Title} script did not compile: {diagnostics}", ex);
        }
    }

    partial void OnScriptExpressionChanged(string value)
    {
        // Drop the cached compilation so the next run picks up the edited expression.
        _compiled = null;
        _compiledFor = null;
    }
}
