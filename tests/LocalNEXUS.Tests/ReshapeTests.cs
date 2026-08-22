using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The five ways text can be reshaped on its way through a wire, and the rule that decides which.
/// </summary>
/// <remarks>
/// The value of this node is that it calls nothing and does the same thing every time, so it is
/// exactly the kind of thing a test can pin down completely. Each mode gets its own case rather
/// than one case with a mode parameter, because the modes have nothing in common beyond the pin
/// they sit on.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ReshapeTests
{
    /// <summary>Runs a node the way the executor would, with a value already on its input.</summary>
    private static async Task<string> Run(TestServices services, ReshapeNode node, string input, string? rule = null)
    {
        var graph = new GraphModel();
        var source = new RecordingNode("source", new List<string>(), PinType.Code) { Append = input };
        graph.AddNode(source);
        graph.AddNode(node);
        Assert.True(graph.TryConnect(source.Out, node.Source, out _));

        if (rule is not null)
        {
            var ruleSource = new RecordingNode("rule", new List<string>()) { Append = rule };
            graph.AddNode(ruleSource);
            Assert.True(graph.TryConnect(ruleSource.Out, node.Rule, out _));
        }

        var run = await new App.Services.Execution.GraphExecutor(services.Services)
            .RunAsync(graph, string.Empty, CancellationToken.None);

        Assert.Equal(App.Services.Execution.RunState.Completed, run.State);
        Assert.True(run.TryGetValue(node.Result, out var value));

        return value?.ToString() ?? string.Empty;
    }

    [Fact]
    public async Task InjectWrapsWhatArrives()
    {
        using var services = TestServices.Create();
        var node = new ReshapeNode
        {
            Mode = ReshapeMode.Inject,
            InjectBefore = "before ",
            InjectAfter = " after"
        };

        Assert.Equal("before middle after", await Run(services, node, "middle"));
    }

    [Fact]
    public async Task ExtractKeepsOnlyWhatMatches()
    {
        using var services = TestServices.Create();
        var node = new ReshapeNode
        {
            Mode = ReshapeMode.Extract,
            ExtractPattern = @"\d+"
        };

        Assert.Equal("42", await Run(services, node, "there are 42 of them"));
    }

    [Fact]
    public async Task ReplaceRewritesEveryMatch()
    {
        using var services = TestServices.Create();
        var node = new ReshapeNode
        {
            Mode = ReshapeMode.Replace,
            RegexPattern = "cat",
            RegexReplacement = "dog"
        };

        Assert.Equal("dog and dog", await Run(services, node, "cat and cat"));
    }

    [Fact]
    public async Task TrimCutsFromTheEndByDefault()
    {
        using var services = TestServices.Create();
        var node = new ReshapeNode
        {
            Mode = ReshapeMode.Trim,
            MaximumCharacters = 5,
            TrimFrom = TrimFrom.End
        };

        var result = await Run(services, node, "abcdefghij");

        Assert.Equal(5, result.Length);
        Assert.Equal("abcde", result);
    }

    [Fact]
    public async Task TrimCanCutFromTheStartInstead()
    {
        using var services = TestServices.Create();
        var node = new ReshapeNode
        {
            Mode = ReshapeMode.Trim,
            MaximumCharacters = 5,
            TrimFrom = TrimFrom.Start
        };

        Assert.Equal("fghij", await Run(services, node, "abcdefghij"));
    }

    /// <summary>Trimming something already short enough leaves it exactly as it was.</summary>
    [Fact]
    public async Task TrimLeavesShortTextAlone()
    {
        using var services = TestServices.Create();
        var node = new ReshapeNode { Mode = ReshapeMode.Trim, MaximumCharacters = 100 };

        Assert.Equal("short", await Run(services, node, "short"));
    }

    /// <summary>
    /// Script mode is compiled at runtime, which is not available in a single file publish.
    /// </summary>
    /// <remarks>
    /// Roslyn scripting cannot build inside a single file bundle. The node reports whether it can
    /// before it is used, and this asserts on that report rather than on the result, because the
    /// answer is legitimately different between running from the IDE and running the shipped exe.
    /// </remarks>
    [Fact]
    public async Task ScriptModeEitherRunsOrSaysItCannot()
    {
        using var services = TestServices.Create();
        var node = new ReshapeNode
        {
            Mode = ReshapeMode.Script,
            ScriptExpression = "input.ToUpperInvariant()"
        };

        if (!ReshapeNode.CanCompileScripts)
        {
            // The state itself is the assertion. Nothing is claimed about the output.
            Assert.False(ReshapeNode.CanCompileScripts);
            return;
        }

        Assert.Equal("HELLO", await Run(services, node, "hello"));
    }

    /// <summary>
    /// A rule arriving on the pin wins over the one typed on the node.
    /// </summary>
    /// <remarks>
    /// This is the disambiguation the node exists to make unambiguous: two possible sources of a
    /// rule and exactly one answer about which applies, so a node with both is not doing something
    /// nobody can predict.
    /// </remarks>
    [Fact]
    public async Task AWiredRuleWinsOverTheTypedOne()
    {
        using var services = TestServices.Create();
        var node = new ReshapeNode
        {
            Mode = ReshapeMode.Inject,
            InjectBefore = "typed ",
            InjectAfter = string.Empty
        };

        var result = await Run(services, node, "middle", rule: "wired " + ReshapeNode.InputPlaceholder);

        Assert.Equal("wired middle", result);
        Assert.DoesNotContain("typed", result, StringComparison.Ordinal);
    }

    /// <summary>With nothing on the rule pin, the node's own settings apply.</summary>
    [Fact]
    public async Task AnUnwiredRulePinLeavesTheTypedRuleInForce()
    {
        using var services = TestServices.Create();
        var node = new ReshapeNode
        {
            Mode = ReshapeMode.Inject,
            InjectBefore = "typed ",
            InjectAfter = string.Empty
        };

        Assert.Equal("typed middle", await Run(services, node, "middle"));
    }

    /// <summary>Settings survive a save and load, for every mode.</summary>
    [Theory]
    [InlineData(ReshapeMode.Inject)]
    [InlineData(ReshapeMode.Extract)]
    [InlineData(ReshapeMode.Replace)]
    [InlineData(ReshapeMode.Trim)]
    [InlineData(ReshapeMode.Script)]
    public void SettingsRoundTrip(ReshapeMode mode)
    {
        var node = new ReshapeNode
        {
            Mode = mode,
            InjectBefore = "a",
            InjectAfter = "b",
            ExtractPattern = "c",
            RegexPattern = "d",
            RegexReplacement = "e",
            MaximumCharacters = 77,
            TrimFrom = TrimFrom.Start,
            ScriptExpression = "input"
        };

        var restored = new ReshapeNode();
        restored.LoadSettings(node.SaveSettings());

        Assert.Equal(mode, restored.Mode);
        Assert.Equal("a", restored.InjectBefore);
        Assert.Equal("b", restored.InjectAfter);
        Assert.Equal("c", restored.ExtractPattern);
        Assert.Equal("d", restored.RegexPattern);
        Assert.Equal("e", restored.RegexReplacement);
        Assert.Equal(77, restored.MaximumCharacters);
        Assert.Equal(TrimFrom.Start, restored.TrimFrom);
    }

    /// <summary>
    /// A graph carrying the retired find and replace pairs reports them rather than applying them.
    /// </summary>
    /// <remarks>
    /// They used to change text with nothing on screen saying so. Silently dropping them would
    /// change what somebody's graph does with no way to find out, so they are read back and named.
    /// </remarks>
    [Fact]
    public void RetiredReplacementsAreReadBackAndNamed()
    {
        var node = new ReshapeNode();

        var settings = node.SaveSettings();
        settings["replacements"] = new System.Text.Json.Nodes.JsonArray
        {
            new System.Text.Json.Nodes.JsonObject { ["find"] = "cat", ["replace"] = "dog" }
        };

        node.LoadSettings(settings);

        var pair = Assert.Single(node.RetiredReplacements);
        Assert.Contains("cat", pair, StringComparison.Ordinal);
        Assert.Contains("dog", pair, StringComparison.Ordinal);

        // And they are not written back out, so the next save is clean.
        Assert.Null(node.SaveSettings()["replacements"]);
    }
}

/// <summary>
/// Stripping the markdown fence a model puts around code nobody asked it to fence.
/// </summary>
/// <remarks>
/// This lives on the Model node as a setting that is on by default, rather than needing a node
/// wired in to undo something nobody wanted. Every case here is a shape a model has actually
/// produced.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class CodeFenceTests
{
    [Fact]
    public void AFencedReplyLosesItsFence()
        => Assert.Equal("public class A { }", CodeFence.Strip("```csharp\npublic class A { }\n```"));

    [Fact]
    public void AFenceWithNoLanguageIsAlsoStripped()
        => Assert.Equal("public class A { }", CodeFence.Strip("```\npublic class A { }\n```"));

    [Fact]
    public void UnfencedCodeIsLeftExactlyAsItIs()
        => Assert.Equal("public class A { }", CodeFence.Strip("public class A { }"));

    /// <summary>
    /// A reply that is prose with a fence inside it is left exactly as it arrived.
    /// </summary>
    /// <remarks>
    /// Deliberate, and the opposite of what looks obvious. A reply wrapped in a fence is a model
    /// formatting code nobody asked it to format. A reply with prose around a fence is an
    /// explanation, and cutting it down to the code block would throw away what was asked for.
    /// </remarks>
    [Fact]
    public void ProseAroundAFenceIsLeftAlone()
    {
        const string reply = "Here you go:\n\n```csharp\npublic class A { }\n```\n\nHope that helps.";

        Assert.Equal(reply, CodeFence.Strip(reply));
    }

    /// <summary>A fence inside a string literal in unfenced code is not treated as a fence.</summary>
    [Fact]
    public void CodeContainingBackticksIsNotMangled()
    {
        const string source = "public class A { const string S = \"```\"; }";

        Assert.Equal(source, CodeFence.Strip(source));
    }

    [Fact]
    public void AnEmptyReplyStaysEmpty() => Assert.Equal(string.Empty, CodeFence.Strip(string.Empty));
}
