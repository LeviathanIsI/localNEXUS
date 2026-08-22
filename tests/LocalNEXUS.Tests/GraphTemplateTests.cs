using System.IO;
using LocalNEXUS.App.Models;
using LocalNEXUS.App.Services.Execution;
using LocalNEXUS.App.Services.Persistence;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The graphs somebody can start from.
/// </summary>
/// <remarks>
/// The bar the templates were asked to clear is that one opens and runs without editing, so these
/// do not stop at counting nodes. Each built in template is applied to a real graph and then run
/// through the real executor with a stubbed model, which is the only thing that can tell the
/// difference between a graph that looks right and a graph that works.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class GraphTemplateTests
{
    public static TheoryData<string> EveryBuiltIn
    {
        get
        {
            var data = new TheoryData<string>();

            using var services = TestServices.Create();

            foreach (var template in new GraphTemplates(services.Factory, new GraphSerializer(services.Factory)).All())
            {
                data.Add(template.Id);
            }

            return data;
        }
    }

    private static GraphTemplates TemplatesFor(TestServices services)
        => new(services.Factory, new GraphSerializer(services.Factory));

    /// <summary>Something ships, and the README shape is among it.</summary>
    [Fact]
    public void TheBuiltInTemplatesAreOffered()
    {
        using var services = TestServices.Create();

        var all = TemplatesFor(services).All();

        Assert.Contains(all, t => t.Id == "minimal");
        Assert.Contains(all, t => t.Id == "multi-file");
        Assert.Contains(all, t => t.Id == "checked");
        Assert.Contains(all, t => t.Id == "debate");

        Assert.All(all, t => Assert.False(t.IsOwn));
        Assert.All(all, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }

    /// <summary>
    /// Every template builds a wired graph.
    /// </summary>
    /// <remarks>
    /// The wires are the part that is tedious and easy to get wrong, so a template with nodes and
    /// no wires would be worse than none: it looks finished.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryBuiltIn))]
    public void ATemplateBuildsAWiredGraph(string id)
    {
        using var services = TestServices.Create();

        var templates = TemplatesFor(services);
        var graph = new GraphModel();

        Assert.Empty(templates.Apply(templates.All().First(t => t.Id == id), graph));

        Assert.NotEmpty(graph.Nodes);
        Assert.NotEmpty(graph.Connections);

        // Nothing is left stranded, which is what an unwired node in a template would be.
        Assert.All(
            graph.Nodes,
            node => Assert.Contains(
                graph.Connections,
                c => c.Source.Owner == node || c.Target.Owner == node));
    }

    /// <summary>
    /// Every template runs, unmodified, to a completed run.
    /// </summary>
    /// <remarks>
    /// The whole bar. A template that needs fixing before it works is worse than no template, and
    /// nothing short of running one can say whether it does. The model is stubbed, because what is
    /// being tested is the shape rather than what a model would say about it.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryBuiltIn))]
    public async Task ATemplateRunsUnmodified(string id)
    {
        using var services = TestServices.Create();
        using var project = SampleProject.Create();

        services.Project.Open(project.Root);
        services.Services.Staging.OpenProject(project.Root);
        await services.Index.EnsureAsync(project.Root, null, CancellationToken.None);

        var templates = TemplatesFor(services);
        var graph = new GraphModel();

        templates.Apply(templates.All().First(t => t.Id == id), graph);

        // "Runs unmodified" means unmodified apart from choosing a model, which is the one thing a
        // template deliberately does not carry: it belongs to the machine rather than to the shape.
        // Self hosted is the provider that needs nothing but an address, and the address goes to
        // the stub, so what is exercised is the graph rather than a model.
        foreach (var model in graph.Nodes.OfType<App.Nodes.ModelNode>())
        {
            model.Provider = App.Nodes.ModelProvider.SelfHosted;
            model.BaseUrl = "http://127.0.0.1:1/v1";
            model.SelfHostedModelId = "stub";
        }

        // A plan first, then code for every file it asks for. The planner shape is what a template
        // with Triage in it needs to get past its first node; the ones without Triage never read it,
        // because the first reply they take is the code.
        services.Models
            .Reply(
                "DECISIONS" + Environment.NewLine
                + "Assets/Scripts/Spinner.cs | EDIT | the spin lives here" + Environment.NewLine
                + Environment.NewLine
                + "PLAN" + Environment.NewLine
                + "1 | EDIT | Assets/Scripts/Spinner.cs | Spinner | spin a little faster")
            .ThenAlways(
                "using UnityEngine;" + Environment.NewLine
                + Environment.NewLine
                + "namespace Game" + Environment.NewLine
                + "{" + Environment.NewLine
                + "    public class Spinner : MonoBehaviour" + Environment.NewLine
                + "    {" + Environment.NewLine
                + "        [SerializeField]" + Environment.NewLine
                + "        private float speed = 90f;" + Environment.NewLine
                + "    }" + Environment.NewLine
                + "}");

        var run = await new GraphExecutor(services.Services).RunAsync(
            graph,
            "Make the Spinner spin faster.",
            CancellationToken.None);

        Assert.True(
            run.State is RunState.Completed or RunState.Unresolved,
            $"{id} ended as {run.State}: {run.FaultMessage}");
    }

    /// <summary>A template replaces what was on the canvas rather than adding to it.</summary>
    [Fact]
    public void ApplyingATemplateReplacesTheCanvas()
    {
        using var services = TestServices.Create();

        var templates = TemplatesFor(services);
        var graph = new GraphModel();

        graph.AddNode(services.Factory.Create("Judge"));

        templates.Apply(templates.All().First(t => t.Id == "minimal"), graph);

        Assert.DoesNotContain(graph.Nodes, n => n.TypeKey == "Judge");
        Assert.Equal(3, graph.Nodes.Count);
    }

    /// <summary>The graph takes the template's name, so the history says what it ran.</summary>
    [Fact]
    public void TheGraphTakesTheTemplateName()
    {
        using var services = TestServices.Create();

        var templates = TemplatesFor(services);
        var template = templates.All().First(t => t.Id == "checked");
        var graph = new GraphModel();

        templates.Apply(template, graph);

        Assert.Equal(template.Name, graph.Name);
    }

    /// <summary>A template named by nothing that exists is refused rather than silently doing nothing.</summary>
    [Fact]
    public void AnUnknownTemplateIsRefused()
    {
        using var services = TestServices.Create();

        var templates = TemplatesFor(services);
        var graph = new GraphModel();

        Assert.Throws<InvalidDataException>(
            () => templates.Apply(new GraphTemplate("kettle", "Kettle", "Nothing.", null), graph));
    }

    /// <summary>Nothing in a template mentions Unity.</summary>
    /// <remarks>
    /// v1.37 made the application work on any codebase, and a set of templates that only made
    /// sense for a Unity project would have undone the visible half of it.
    /// </remarks>
    [Fact]
    public void NoTemplateAssumesUnity()
    {
        using var services = TestServices.Create();

        foreach (var template in TemplatesFor(services).All())
        {
            Assert.DoesNotContain("Unity", template.Name, StringComparison.Ordinal);
            Assert.DoesNotContain("Unity", template.Description, StringComparison.Ordinal);
            Assert.DoesNotContain("MonoBehaviour", template.Description, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Saving a graph as a template and starting from it again produces the same graph.
    /// </summary>
    /// <remarks>
    /// Written to a scratch folder rather than the real one, because a test must not put files
    /// where the application keeps its own.
    /// </remarks>
    [Fact]
    public void AGraphSavedAsATemplateComesBack()
    {
        using var services = TestServices.Create();

        var serializer = new GraphSerializer(services.Factory);
        var templates = TemplatesFor(services);

        var built = new GraphModel();
        templates.Apply(templates.All().First(t => t.Id == "checked"), built);

        var folder = Path.Combine(Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "mine" + GraphSerializer.FileExtension);
            serializer.Save(built, path);

            var mine = new GraphTemplate("mine", "Mine", "Saved here.", path);
            Assert.True(mine.IsOwn);

            var reopened = new GraphModel();
            Assert.Empty(templates.Apply(mine, reopened));

            Assert.Equal(built.Nodes.Count, reopened.Nodes.Count);
            Assert.Equal(built.Connections.Count, reopened.Connections.Count);
            Assert.Equal("Mine", reopened.Name);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Not the test's problem.
            }
        }
    }
}
