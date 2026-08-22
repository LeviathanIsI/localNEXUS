using LocalNEXUS.App.Services.Planning;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// What the open project contains, read by parsing rather than through a workspace.
/// </summary>
/// <remarks>
/// The index is what stops the coder inventing a second copy of something the project already
/// has, so a gap in it is not a missing feature, it is a duplicate type nobody notices until two
/// half wired versions of the same component are in the scene.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ProjectIndexTests
{
    private static async Task<ProjectIndexService> IndexOf(SampleProject project)
    {
        var index = new ProjectIndexService();
        await index.EnsureAsync(project.Root, null, CancellationToken.None);
        return index;
    }

    [Fact]
    public async Task EveryScriptIsFound()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        Assert.True(index.IsReady);
        Assert.Equal(4, index.Files.Count);
    }

    [Fact]
    public async Task ATypeIsFoundByName()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        var found = Assert.Single(index.FindType("Health"));

        Assert.Equal("Game", found.Namespace);
        Assert.Equal("Game.Health", found.FullName);
        Assert.Contains("IDamageable", found.BaseTypes);
    }

    /// <summary>A MonoBehaviour is recognised as one, because every Unity rule turns on it.</summary>
    [Fact]
    public async Task AMonoBehaviourIsRecognised()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        var spinner = Assert.Single(index.FindType("Spinner"));

        Assert.True(spinner.IsMonoBehaviour);
        Assert.Contains(spinner.SerializedFields, f => f.Name == "speed");
    }

    /// <summary>A type nobody declared is not found, rather than found empty.</summary>
    [Fact]
    public async Task ATypeThatDoesNotExistIsNotFound()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        Assert.Empty(index.FindType("NotInThisProject"));
        Assert.Empty(index.FindType(null));
    }

    /// <summary>A file added after the index was built is picked up when it is rebuilt.</summary>
    [Fact]
    public async Task ANewFileIsPickedUpOnRebuild()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        Assert.Empty(index.FindType("Later"));

        project.Write("Later.cs", "namespace Game { public class Later { } }");
        await index.EnsureAsync(project.Root, null, CancellationToken.None);

        Assert.Single(index.FindType("Later"));
    }

    /// <summary>A file is found by its path however the path was spelled.</summary>
    [Fact]
    public async Task AFileIsFoundByPathRegardlessOfSeparator()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        Assert.NotNull(index.FindFile("Assets/Scripts/Health.cs"));
        Assert.NotNull(index.FindFile("Assets\\Scripts\\Health.cs"));
    }

    /// <summary>Forgetting the project leaves nothing behind for the next one to inherit.</summary>
    [Fact]
    public async Task ForgettingClearsTheIndex()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        index.Forget();

        Assert.Empty(index.Files);
        Assert.Null(index.IndexedProject);
    }

    /// <summary>
    /// A type the project already has is refused, and the refusal names where it is.
    /// </summary>
    /// <remarks>
    /// Enforced here rather than asked of the model, because the shortest path for a coder is
    /// always a new file. A refusal that does not say where the existing type lives is not
    /// actionable, so the path is part of the assertion.
    /// </remarks>
    [Fact]
    public async Task AnExistingTypeIsRefusedByName()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        var verdict = DuplicateTypeGuard.Check(
            index,
            "InventorySlot",
            "Assets/Scripts/Inventory/InventorySlot.cs",
            Array.Empty<string>());

        Assert.False(verdict.Allowed);
        Assert.Contains("InventorySlot", verdict.Message, StringComparison.Ordinal);
        Assert.NotNull(verdict.ExistingPath);
    }

    /// <summary>A type nothing has yet is allowed.</summary>
    [Fact]
    public async Task ANewTypeIsAllowed()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        var verdict = DuplicateTypeGuard.Check(
            index,
            "SomethingBrandNew",
            "Assets/Scripts/SomethingBrandNew.cs",
            Array.Empty<string>());

        Assert.True(verdict.Allowed);
    }

    /// <summary>A plan that creates the same type twice is refused on the second one.</summary>
    /// <remarks>
    /// A plan is a list, and nothing about the list stops the same type appearing on two rows.
    /// Both would compile alone and the second would overwrite the first.
    /// </remarks>
    [Fact]
    public async Task APlanCannotCreateTheSameTypeTwice()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        var verdict = DuplicateTypeGuard.Check(
            index,
            "Weapon",
            "Assets/Scripts/Weapon.cs",
            new[] { "Weapon" });

        Assert.False(verdict.Allowed);
        Assert.Contains("Weapon", verdict.Message, StringComparison.Ordinal);
    }

    /// <summary>Filtering a plan keeps what is allowed and reports what was blocked.</summary>
    [Fact]
    public async Task FilteringAPlanSeparatesAllowedFromBlocked()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        var draft = new[]
        {
            new CodeTask(0, "Assets/Scripts/Weapon.cs", "Weapon", FileOperation.Create, "new", "", null),
            new CodeTask(1, "Assets/Scripts/Inventory/InventorySlot.cs", "InventorySlot", FileOperation.Create, "new", "", null)
        };

        var (allowed, blocked) = DuplicateTypeGuard.Filter(index, draft);

        Assert.Single(allowed);
        Assert.Equal("Weapon", allowed[0].TypeName);
        Assert.Single(blocked);
    }

    /// <summary>An edit to a file that exists is not a duplicate, because it is not a creation.</summary>
    [Fact]
    public async Task AnEditIsNotBlockedAsADuplicate()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);

        var draft = new[]
        {
            new CodeTask(0, "Assets/Scripts/InventorySlot.cs", "InventorySlot", FileOperation.Edit, "add a field", "", "existing")
        };

        var (allowed, blocked) = DuplicateTypeGuard.Filter(index, draft);

        Assert.Single(allowed);
        Assert.Empty(blocked);
    }
}
