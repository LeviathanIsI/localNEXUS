using System.IO;
using System.Text.Json.Nodes;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Nodes;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Saving a graph and getting the same graph back.
/// </summary>
/// <remarks>
/// Two separate bugs have silently dropped nodes and their wires on load, and both were found by
/// someone opening a graph they had spent an afternoon on rather than by anything in the code
/// complaining. That is the whole reason this file exists, and it is why the assertions are about
/// counts and identities rather than about the shape of the JSON: a document that reads back
/// differently is a failure whatever it looks like on disk.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class SerializationTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N"));

    public SerializationTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string PathFor(string name) => Path.Combine(_folder, name + GraphSerializer.FileExtension);

    /// <summary>Every type key the factory answers to, current and historical.</summary>
    public static TheoryData<string> EveryTypeKey => new()
    {
        "Prompt", "Input",
        "Triage", "Plan",
        "Model",
        "Debate",
        "Judge",
        "Reshape", "Patch", "Transform",
        "CompilerCheck", "CompileCheck", "Compile",
        "Output"
    };

    /// <summary>
    /// A graph saved under any key this application has ever written still opens.
    /// </summary>
    /// <remarks>
    /// Nodes have been renamed twice, Patch to Reshape and CompileCheck to CompilerCheck, and each
    /// rename is a graph somebody saved before it. A key that no longer resolves does not fail
    /// loudly; it produces a graph with a node missing and the wires around it gone.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryTypeKey))]
    public void EveryHistoricalTypeKeyStillBuildsANode(string typeKey)
    {
        using var services = TestServices.Create();

        var node = services.Factory.Create(typeKey);

        Assert.NotNull(node);
        Assert.IsNotType<UnavailableNode>(node);
    }

    /// <summary>Every entry the palette offers, as the palette offers it.</summary>
    public static TheoryData<string> EveryPaletteKey
    {
        get
        {
            var keys = new TheoryData<string>();

            foreach (var descriptor in NodeFactory.Descriptors)
            {
                keys.Add(descriptor.TypeKey);
            }

            return keys;
        }
    }

    /// <summary>
    /// Everything on the palette can be added, and calls itself what the palette called it.
    /// </summary>
    /// <remarks>
    /// The test that would have caught both occurrences of the same bug. The palette offered
    /// Compile while the node saved itself as CompileCheck, and later offered Reshape, Debate and
    /// Judge while the factory had never heard of any of them. Both shipped.
    ///
    /// It is driven from <see cref="NodeFactory.Descriptors"/> rather than from a list written out
    /// here, because a list written out here is a third place to forget. A node type added to the
    /// application arrives in this test without anybody adding it.
    ///
    /// The second assertion is the half that is easy to miss: a type that constructs but reports a
    /// different key writes a graph it cannot then reopen.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryPaletteKey))]
    public void EveryPaletteEntryConstructsAndKeepsItsKey(string typeKey)
    {
        using var services = TestServices.Create();

        var node = services.Factory.Create(typeKey);

        Assert.IsNotType<UnavailableNode>(node);
        Assert.Equal(typeKey, node.TypeKey);
    }

    /// <summary>Every palette entry has a label and a description, because both are shown.</summary>
    [Fact]
    public void EveryPaletteEntryIsLabelled()
    {
        Assert.NotEmpty(NodeFactory.Descriptors);

        Assert.All(NodeFactory.Descriptors, descriptor =>
        {
            Assert.False(string.IsNullOrWhiteSpace(descriptor.TypeKey));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
        });
    }

    /// <summary>A graph saved with historical keys loads whole, with every wire.</summary>
    [Fact]
    public void AGraphSavedUnderOldKeysLoadsWhole()
    {
        using var services = TestServices.Create();
        var serializer = new GraphSerializer(services.Factory);

        var saved = new GraphModel();
        var prompt = (PromptNode)services.Factory.Create("Prompt");
        var triage = (TriageNode)services.Factory.Create("Triage");
        var model = (ModelNode)services.Factory.Create("Model");
        var reshape = (ReshapeNode)services.Factory.Create("Reshape");
        var check = (CompilerCheckNode)services.Factory.Create("CompilerCheck");
        var output = (OutputNode)services.Factory.Create("Output");

        foreach (var node in new NodeBase[] { prompt, triage, model, reshape, check, output })
        {
            saved.AddNode(node);
        }

        Assert.True(saved.TryConnect(prompt.Request, triage.Request, out _));
        Assert.True(saved.TryConnect(model.Self, triage.Model, out _));
        Assert.True(saved.TryConnect(triage.Plan, model.Prompt, out _));
        Assert.True(saved.TryConnect(model.Completion, reshape.Source, out _));
        Assert.True(saved.TryConnect(reshape.Result, check.Code, out _));
        Assert.True(saved.TryConnect(check.Checked, output.Content, out _));

        var wires = saved.Connections.Count;
        var path = PathFor("old-keys");
        serializer.Save(saved, path);

        // Rewrite the type keys to the names an older build wrote. Everything else about the
        // document, including the pin identifiers the wires refer to, stays exactly as saved.
        var document = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        var historical = new Dictionary<string, string>
        {
            ["Prompt"] = "Input",
            ["Triage"] = "Plan",
            ["Reshape"] = "Patch",
            ["CompilerCheck"] = "CompileCheck"
        };

        foreach (var element in ((JsonArray)document["nodes"]!).OfType<JsonObject>())
        {
            if (historical.TryGetValue(element["type"]!.GetValue<string>(), out var old))
            {
                element["type"] = old;
            }
        }

        File.WriteAllText(path, document.ToJsonString());

        var loaded = new GraphModel();
        var warnings = serializer.LoadInto(loaded, path);

        Assert.Empty(warnings);
        Assert.Equal(6, loaded.Nodes.Count);
        Assert.Equal(wires, loaded.Connections.Count);
        Assert.Contains(loaded.Nodes, n => n is ReshapeNode);
        Assert.Contains(loaded.Nodes, n => n is CompilerCheckNode);
        Assert.Contains(loaded.Nodes, n => n is TriageNode);

        // A rename is reported as a note rather than as an error, because the graph opened whole.
        Assert.NotEmpty(serializer.Migrations);
    }

    /// <summary>A round trip preserves nodes, titles, positions, settings and wires.</summary>
    [Fact]
    public void ARoundTripPreservesTheGraph()
    {
        using var services = TestServices.Create();
        var serializer = new GraphSerializer(services.Factory);

        var saved = new GraphModel();
        var reshape = (CompilerCheckNode)services.Factory.Create("CompilerCheck");
        var output = (OutputNode)services.Factory.Create("Output");

        reshape.Title = "shape it";
        reshape.X = 123d;
        reshape.Y = 456d;

        saved.AddNode(reshape);
        saved.AddNode(output);
        saved.TryConnect(reshape.Checked, output.Content, out _);

        var path = PathFor("round-trip");
        serializer.Save(saved, path);

        var loaded = new GraphModel();
        Assert.Empty(serializer.LoadInto(loaded, path));

        Assert.Equal(2, loaded.Nodes.Count);
        Assert.Single(loaded.Connections);

        var restoredReshape = loaded.Nodes.OfType<CompilerCheckNode>().Single();
        Assert.Equal("shape it", restoredReshape.Title);
        Assert.Equal(123d, restoredReshape.X);
        Assert.Equal(456d, restoredReshape.Y);
        Assert.Equal(reshape.Id, restoredReshape.Id);
    }

    /// <summary>
    /// A pin renamed since the graph was saved keeps its wire, by position.
    /// </summary>
    /// <remarks>
    /// This is the v1.2 bug stated as a test. Pins are matched by name first and by position
    /// second, which is what makes renaming a pin safe and inserting one in the middle
    /// catastrophic.
    /// </remarks>
    [Fact]
    public void ARenamedPinKeepsItsWire()
    {
        using var services = TestServices.Create();
        var serializer = new GraphSerializer(services.Factory);

        var saved = new GraphModel();
        var reshape = (CompilerCheckNode)services.Factory.Create("CompilerCheck");
        var output = (OutputNode)services.Factory.Create("Output");
        saved.AddNode(reshape);
        saved.AddNode(output);
        saved.TryConnect(reshape.Checked, output.Content, out _);

        var path = PathFor("renamed-pin");
        serializer.Save(saved, path);

        var document = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

        foreach (var element in ((JsonArray)document["nodes"]!).OfType<JsonObject>())
        {
            foreach (var pin in ((JsonArray)element["outputs"]!).OfType<JsonObject>())
            {
                pin["name"] = "WhateverItUsedToBeCalled";
            }
        }

        File.WriteAllText(path, document.ToJsonString());

        var loaded = new GraphModel();
        serializer.LoadInto(loaded, path);

        Assert.Single(loaded.Connections);
    }

    /// <summary>
    /// A node type this build does not know is held rather than dropped, with a Code wire.
    /// </summary>
    /// <remarks>
    /// An extension that is not installed here must not cost somebody their wiring, and above all
    /// must not have the hole written back out the next time the graph is saved.
    ///
    /// The wire here is deliberately Code rather than Text. A placeholder used to rebuild every
    /// pin as Text, which passes a test that wires Text and drops the wire in the case that
    /// actually matters, because a node between a coder and a writer is carrying Code.
    /// </remarks>
    [Fact]
    public void AnUnknownTypeIsHeldAsAPlaceholderWithItsWires()
    {
        using var services = TestServices.Create();
        var serializer = new GraphSerializer(services.Factory);

        var saved = new GraphModel();
        var reshape = (CompilerCheckNode)services.Factory.Create("CompilerCheck");
        var output = (OutputNode)services.Factory.Create("Output");
        saved.AddNode(reshape);
        saved.AddNode(output);
        saved.TryConnect(reshape.Checked, output.Content, out _);

        var path = PathFor("unknown-type");
        serializer.Save(saved, path);

        var document = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        var first = ((JsonArray)document["nodes"]!)
            .OfType<JsonObject>()
            .First(n => n["type"]!.GetValue<string>() == "CompilerCheck");

        first["type"] = "SomeExtensionNobodyInstalled";
        File.WriteAllText(path, document.ToJsonString());

        var loaded = new GraphModel();
        var warnings = serializer.LoadInto(loaded, path);

        Assert.NotEmpty(warnings);
        Assert.Equal(2, loaded.Nodes.Count);
        Assert.Single(loaded.Connections);

        var placeholder = Assert.Single(loaded.Nodes.OfType<UnavailableNode>());

        // The pin is Code because the file said so, which is the only way a placeholder can know.
        Assert.Equal(PinType.Code, Assert.Single(placeholder.Outputs).PinType);
    }

    /// <summary>The type of every pin is written, so a node this build cannot make can read it.</summary>
    [Fact]
    public void PinTypesAreWrittenToTheFile()
    {
        using var services = TestServices.Create();
        var serializer = new GraphSerializer(services.Factory);

        var saved = new GraphModel();
        var triage = (TriageNode)services.Factory.Create("Triage");
        saved.AddNode(triage);

        var path = PathFor("pin-types");
        serializer.Save(saved, path);

        var element = ((JsonArray)((JsonObject)JsonNode.Parse(File.ReadAllText(path))!)["nodes"]!)
            .OfType<JsonObject>()
            .Single();

        var written = ((JsonArray)element["inputs"]!)
            .OfType<JsonObject>()
            .Select(p => p["type"]?.GetValue<string>())
            .ToList();

        Assert.Equal(triage.Inputs.Select(p => p.PinType.ToString()), written);
    }

    /// <summary>
    /// A graph saved before pin types were written still opens, with its pins defaulting to Text.
    /// </summary>
    /// <remarks>
    /// The old behaviour, kept for old documents. Only a placeholder reads the field at all, and
    /// only a placeholder in a graph saved by an older build is affected, so the fallback is the
    /// same answer those graphs already get today.
    /// </remarks>
    [Fact]
    public void AGraphWithNoPinTypesStillLoads()
    {
        using var services = TestServices.Create();
        var serializer = new GraphSerializer(services.Factory);

        var saved = new GraphModel();
        var check = (CompilerCheckNode)services.Factory.Create("CompilerCheck");
        var output = (OutputNode)services.Factory.Create("Output");
        saved.AddNode(check);
        saved.AddNode(output);
        saved.TryConnect(check.Checked, output.Content, out _);

        var path = PathFor("no-pin-types");
        serializer.Save(saved, path);

        var document = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;

        foreach (var element in ((JsonArray)document["nodes"]!).OfType<JsonObject>())
        {
            foreach (var pin in ((JsonArray)element["inputs"]!).Concat((JsonArray)element["outputs"]!).OfType<JsonObject>())
            {
                pin.Remove("type");
            }
        }

        File.WriteAllText(path, document.ToJsonString());

        var loaded = new GraphModel();

        Assert.Empty(serializer.LoadInto(loaded, path));
        Assert.Equal(2, loaded.Nodes.Count);
        Assert.Single(loaded.Connections);
    }

    /// <summary>A graph written by a newer build is refused rather than half read.</summary>
    [Fact]
    public void ANewerFormatIsRefused()
    {
        using var services = TestServices.Create();
        var serializer = new GraphSerializer(services.Factory);

        var path = PathFor("from-the-future");
        File.WriteAllText(path, new JsonObject
        {
            ["version"] = GraphSerializer.FormatVersion + 1,
            ["nodes"] = new JsonArray(),
            ["connections"] = new JsonArray()
        }.ToJsonString());

        Assert.Throws<InvalidDataException>(() => serializer.LoadInto(new GraphModel(), path));
    }

    /// <summary>Loading replaces what was open rather than merging into it.</summary>
    [Fact]
    public void LoadingClearsWhatWasAlreadyThere()
    {
        using var services = TestServices.Create();
        var serializer = new GraphSerializer(services.Factory);

        var saved = new GraphModel();
        saved.AddNode(services.Factory.Create("Output"));

        var path = PathFor("replaces");
        serializer.Save(saved, path);

        var target = new GraphModel();
        target.AddNode(services.Factory.Create("Output"));
        target.AddNode(services.Factory.Create("CompilerCheck"));

        serializer.LoadInto(target, path);

        Assert.Single(target.Nodes);
    }
    /// <summary>
    /// A node saved under its own current key reopens as itself.
    /// </summary>
    /// <remarks>
    /// The narrowest possible statement of the rule, and the one that is worth stating: the key a
    /// node writes when the graph is saved has to be a key the factory answers to. Nothing else in
    /// the application checks that the two agree, and when they do not the graph does not fail to
    /// open, it opens with a placeholder where the node was.
    /// </remarks>
    [Theory]
    [InlineData("Prompt")]
    [InlineData("Triage")]
    [InlineData("Model")]
    [InlineData("Reshape")]
    [InlineData("CompilerCheck")]
    [InlineData("Output")]
    public void ANodeReopensAsItself(string typeKey)
    {
        using var services = TestServices.Create();
        var serializer = new GraphSerializer(services.Factory);

        var saved = new GraphModel();

        var node = services.Factory.Create(typeKey);
        Assert.Equal(typeKey, node.TypeKey);

        saved.AddNode(node);

        var path = PathFor("reopens-" + typeKey);
        serializer.Save(saved, path);

        var loaded = new GraphModel();
        serializer.LoadInto(loaded, path);

        Assert.IsNotType<UnavailableNode>(Assert.Single(loaded.Nodes));
    }
}
