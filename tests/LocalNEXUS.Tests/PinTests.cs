using LocalNEXUS.App.Models;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The one table that decides what may join to what.
/// </summary>
/// <remarks>
/// Small enough to state exhaustively, and worth stating exhaustively: every rule here was added
/// for a reason that is not obvious from the code, and a change that looks harmless can quietly
/// allow a wire nobody meant to allow.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class PinTests
{
    [Theory]
    [InlineData(PinType.Text, PinType.Text, true)]
    [InlineData(PinType.Code, PinType.Code, true)]
    [InlineData(PinType.Model, PinType.Model, true)]
    public void MatchingTypesAlwaysFlow(PinType source, PinType target, bool expected)
        => Assert.Equal(expected, PinTypeCompatibility.CanFlow(source, target));

    /// <summary>
    /// Code into Text is the one exception, and it only goes one way.
    /// </summary>
    /// <remarks>
    /// Without it a model node, which takes Text and emits Code, could only ever be fed by an
    /// input node. With it going the other way, arbitrary prose could reach the pin that writes
    /// files.
    /// </remarks>
    [Fact]
    public void CodeFlowsIntoTextButNotTheReverse()
    {
        Assert.True(PinTypeCompatibility.CanFlow(PinType.Code, PinType.Text));
        Assert.False(PinTypeCompatibility.CanFlow(PinType.Text, PinType.Code));
    }

    /// <summary>
    /// A model is a reference and is never coerced.
    /// </summary>
    /// <remarks>
    /// The failure this guards is a wire meaning "use this model" turning into one meaning "paste
    /// this model's name", which compiles and runs and produces nonsense.
    /// </remarks>
    [Theory]
    [InlineData(PinType.Model, PinType.Text)]
    [InlineData(PinType.Model, PinType.Code)]
    [InlineData(PinType.Text, PinType.Model)]
    [InlineData(PinType.Code, PinType.Model)]
    public void ModelJoinsOnlyToModel(PinType source, PinType target)
        => Assert.False(PinTypeCompatibility.CanFlow(source, target));

    [Fact]
    public void AGraphRefusesAWireThatDoesNotTypecheck()
    {
        var graph = new GraphModel();
        var prompt = new App.Nodes.PromptNode();
        var output = new App.Nodes.OutputNode();

        graph.AddNode(prompt);
        graph.AddNode(output);

        var joined = graph.TryConnect(prompt.Request, output.Content, out var why);

        Assert.False(joined);
        Assert.NotEmpty(why);
    }

    [Fact]
    public void AGraphAcceptsAWireThatDoes()
    {
        var graph = new GraphModel();
        var reshape = new App.Nodes.ReshapeNode();
        var output = new App.Nodes.OutputNode();

        graph.AddNode(reshape);
        graph.AddNode(output);

        Assert.True(graph.TryConnect(reshape.Result, output.Content, out _));
    }

    /// <summary>Removing a node takes its wires with it, rather than leaving them pointing at nothing.</summary>
    [Fact]
    public void RemovingANodeRemovesItsWires()
    {
        var graph = new GraphModel();
        var reshape = new App.Nodes.ReshapeNode();
        var output = new App.Nodes.OutputNode();

        graph.AddNode(reshape);
        graph.AddNode(output);
        graph.TryConnect(reshape.Result, output.Content, out _);

        Assert.Single(graph.Connections);

        graph.RemoveNode(reshape);

        Assert.Empty(graph.Connections);
    }
}
