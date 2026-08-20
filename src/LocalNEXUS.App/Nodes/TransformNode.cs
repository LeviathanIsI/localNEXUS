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
/// Template mode covers the common case of wrapping or lightly editing a value. Script mode
/// exists for everything else and is compiled through Roslyn. Compilation is cached against the
/// expression text, so a graph that runs the same transform repeatedly pays for it once.
///
/// It passes repair requests through rather than answering them, because it did not write the
/// code and cannot fix it. Its own upstream is asked, and whatever comes back is put through this
/// transform on the way out. That matters for the ordinary pipeline, where this node is what
/// strips a markdown fence from a model reply: without the pass through, a repaired reply would
/// arrive at the compiler still wrapped in one and could never compile.
/// </remarks>
public sealed partial class TransformNode : NodeBase, ICodeRepairSource
{
    /// <summary>The placeholder replaced with the incoming value in template mode.</summary>
    public const string InputPlaceholder = "{{input}}";

    /// <summary>
    /// The starting expression. Model replies often arrive wrapped in a markdown code fence even
    /// when the prompt asks otherwise, and a fenced reply is not a valid C# file, so the default
    /// unwraps one when it is present and leaves anything else untouched.
    /// </summary>
    public const string DefaultScriptExpression =
        "Regex.Replace(input.Trim(), @\"(?s)^```[A-Za-z0-9#+_-]*\\s*\\r?\\n(.*?)\\r?\\n?```$\", \"$1\").Trim()";

    private static readonly ScriptOptions ScriptCompilationOptions = ScriptOptions.Default
        .WithImports(
            "System",
            "System.Collections.Generic",
            "System.Linq",
            "System.Text",
            "System.Text.RegularExpressions")
        .WithReferences(
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(Regex).Assembly);

    /// <summary>Which transform is applied.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTemplateMode))]
    [NotifyPropertyChangedFor(nameof(IsScriptMode))]
    private TransformMode _mode = TransformMode.Template;

    /// <summary>The template applied in template mode. Occurrences of <c>{{input}}</c> are substituted.</summary>
    [ObservableProperty]
    private string _template = InputPlaceholder;

    /// <summary>The C# expression evaluated in script mode.</summary>
    [ObservableProperty]
    private string _scriptExpression = DefaultScriptExpression;

    private ScriptRunner<object>? _compiled;
    private string? _compiledFor;

    public TransformNode()
        : base("Transform")
    {
        Source = AddInput("Code", PinType.Code);
        Result = AddOutput("Code", PinType.Code);
    }

    /// <summary>Receives the value to rewrite.</summary>
    public Pin Source { get; }

    /// <summary>Carries the rewritten value onwards.</summary>
    public Pin Result { get; }

    /// <summary>Literal substitutions applied after the template is filled.</summary>
    public ObservableCollection<FindReplacePair> Replacements { get; } = new();

    /// <inheritdoc />
    public override string TypeKey => "Transform";

    /// <summary>True when template mode is selected. Drives which editor is shown.</summary>
    public bool IsTemplateMode => Mode == TransformMode.Template;

    /// <summary>True when script mode is selected.</summary>
    public bool IsScriptMode => Mode == TransformMode.Script;

    /// <inheritdoc />
    public override async Task<NodeResult> ExecuteAsync(NodeExecutionContext ctx, CancellationToken ct)
    {
        var input = ctx.GetText(Source);

        var output = Mode switch
        {
            TransformMode.Template => ApplyTemplate(input),
            TransformMode.Script => await RunScriptAsync(input, ct).ConfigureAwait(false),
            _ => input
        };

        StatusMessage = $"{Mode}: {input.Length} to {output.Length} characters";
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
            ["template"] = Template,
            ["scriptExpression"] = ScriptExpression,
            ["replacements"] = replacements
        };
    }

    /// <inheritdoc />
    public override void LoadSettings(JsonObject settings)
    {
        if (Enum.TryParse<TransformMode>(settings["mode"]?.GetValue<string>(), out var mode))
        {
            Mode = mode;
        }

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
        return Mode switch
        {
            TransformMode.Template => ApplyTemplate(revised),
            TransformMode.Script => await RunScriptAsync(revised, ct).ConfigureAwait(false),
            _ => revised
        };
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

    private string ApplyTemplate(string input)
    {
        var output = (Template ?? string.Empty).Replace(InputPlaceholder, input, StringComparison.Ordinal);

        foreach (var pair in Replacements)
        {
            if (!string.IsNullOrEmpty(pair.Find))
            {
                output = output.Replace(pair.Find, pair.Replace ?? string.Empty, StringComparison.Ordinal);
            }
        }

        return output;
    }

    private async Task<string> RunScriptAsync(string input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ScriptExpression))
        {
            return input;
        }

        var runner = GetOrCompileRunner();

        object? value;
        try
        {
            value = await runner(new TransformScriptGlobals { input = input }, ct).ConfigureAwait(false);
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

    private ScriptRunner<object> GetOrCompileRunner()
    {
        if (_compiled is not null && string.Equals(_compiledFor, ScriptExpression, StringComparison.Ordinal))
        {
            return _compiled;
        }

        try
        {
            var script = CSharpScript.Create<object>(ScriptExpression, ScriptCompilationOptions, typeof(TransformScriptGlobals));
            _compiled = script.CreateDelegate();
            _compiledFor = ScriptExpression;
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
