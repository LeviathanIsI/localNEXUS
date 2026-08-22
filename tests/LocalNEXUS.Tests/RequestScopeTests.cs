using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// Whether a request says enough to plan from.
/// </summary>
/// <remarks>
/// The line is narrow on purpose, and it is not "seems vague". A request qualifies if it names
/// something the project already has, or if it puts forward a name of its own, and either is
/// enough. Only a request doing neither is stopped.
///
/// That second half is what keeps it off ordinary work. Every request to create something names
/// nothing that exists yet, by definition, so a test of "does the index know this" alone would
/// stop every create in the set.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class RequestScopeTests
{
    private static async Task<ProjectIndexService> IndexOf(SampleProject project)
    {
        var index = new ProjectIndexService();
        await index.EnsureAsync(project.Root, null, CancellationToken.None);
        return index;
    }

    /// <summary>
    /// A request naming nothing at all is stopped.
    /// </summary>
    /// <remarks>
    /// The failing case, verbatim. Two words, no type, no file, no member, nothing introduced.
    /// Planning it meant editing all five files of a project and inventing a method on one.
    /// </remarks>
    [Theory]
    [InlineData("Make it faster.")]
    [InlineData("make it faster")]
    [InlineData("Clean this up.")]
    [InlineData("Improve the code.")]
    public async Task ARequestNamingNothingIsNotPlannable(string request)
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        Assert.False(RequestScope.IsPlannable(request, index));
    }

    /// <summary>A request naming one real type plans, without anybody being asked.</summary>
    [Theory]
    [InlineData("Add a Heal method to the existing Health class.")]
    [InlineData("Rename the speed field on Spinner.")]
    [InlineData("Health needs an upper limit.")]
    [InlineData("Change Assets/Scripts/InventorySlot.cs to hold a stack size.")]
    [InlineData("Make TakeDamage clamp at zero.")]
    public async Task ARequestNamingSomethingRealIsPlannable(string request)
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        Assert.True(RequestScope.IsPlannable(request, index));
    }

    /// <summary>
    /// A request introducing a name of its own plans, even though nothing by that name exists.
    /// </summary>
    /// <remarks>
    /// The half that keeps this off ordinary work. Every one of these names nothing in the index,
    /// because none of them exists yet, and every one is a perfectly clear thing to do.
    /// </remarks>
    [Theory]
    [InlineData("Add a Cooldown class that tracks a duration in seconds.")]
    [InlineData("Add a DamageType enum with Physical, Fire and Poison values.")]
    [InlineData("Add an IPickup interface with an OnPickedUp method.")]
    [InlineData("Add a static StringUtil class.")]
    [InlineData("Write a `LapTimer` that records lap durations.")]
    public async Task ARequestIntroducingANameIsPlannable(string request)
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        Assert.False(RequestScope.NamesSomethingExisting(request, index));
        Assert.True(RequestScope.IsPlannable(request, index));
    }

    /// <summary>
    /// The first word of a sentence is not a name, which is the whole of the distinction.
    /// </summary>
    /// <remarks>
    /// "Make it faster." and "Add a Cooldown class." both contain exactly one capitalised word
    /// outside the index. Only one of them is introducing a name, and where it sits in the
    /// sentence is what says which.
    /// </remarks>
    [Fact]
    public void TheFirstWordOfASentenceIsNotAName()
    {
        Assert.False(RequestScope.IntroducesAName("Make it faster."));
        Assert.False(RequestScope.IntroducesAName("Optimise everything."));
        Assert.True(RequestScope.IntroducesAName("Add a Cooldown class."));
        Assert.True(RequestScope.IntroducesAName("Make the Cooldown faster."));
    }

    /// <summary>The questions carry concrete options taken from the project.</summary>
    [Fact]
    public async Task TheQuestionCarriesOptionsFromTheIndex()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        var question = Assert.Single(RequestScope.AskWhichOne("Make it faster.", index, Array.Empty<RankedFile>()));

        Assert.True(question.IsAnswerable);
        Assert.True(question.Options.Count >= 2);
        Assert.True(question.Options.Count <= RequestScope.MaximumOptions);

        // Everything offered is something the project actually declares.
        var declared = index.Files.SelectMany(f => f.Types.Select(t => t.Name)).ToHashSet(StringComparer.Ordinal);

        Assert.All(question.Options, option => Assert.Contains(option, declared));

        // And the request is quoted back, so the person can see what was not understood.
        Assert.Contains("Make it faster.", question.Text, StringComparison.Ordinal);
    }

    /// <summary>The ranking is offered first when it has something to say.</summary>
    /// <remarks>
    /// It usually has nothing to say about a request naming nothing, since it works from terms the
    /// request shares with the project. When it does, it is the application's own answer to what
    /// the request is about and there is no reason to ignore it.
    /// </remarks>
    [Fact]
    public async Task TheRankingIsOfferedFirst()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        var spinner = index.FindFile("Assets/Scripts/Spinner.cs");
        Assert.NotNull(spinner);

        var ranked = new[] { new RankedFile(spinner, 1d, new[] { "spinner" }) };
        var question = Assert.Single(RequestScope.AskWhichOne("Make it faster.", index, ranked));

        Assert.Equal("Spinner", question.Options[0]);
    }

    /// <summary>
    /// A project with too little in it to choose between is not stopped.
    /// </summary>
    /// <remarks>
    /// A question with one option is not a question. There is nothing to be gained by halting a run
    /// to ask it, so the run carries on and the model plans as it always did.
    /// </remarks>
    [Fact]
    public async Task AProjectWithNothingToChooseBetweenIsNotAsked()
    {
        using var project = SampleProject.Create();

        foreach (var name in new[] { "Health.cs", "InventorySlot.cs", "Spinner.cs" })
        {
            System.IO.File.Delete(project.PathTo(name));
        }

        var index = await IndexOf(project);

        Assert.Empty(RequestScope.AskWhichOne("Make it faster.", index, Array.Empty<RankedFile>()));
    }

    /// <summary>An empty request is not plannable, and was not before this either.</summary>
    [Fact]
    public async Task AnEmptyRequestIsNotPlannable()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        Assert.False(RequestScope.IsPlannable(string.Empty, index));
        Assert.False(RequestScope.IsPlannable("   ", index));
    }
}
