using System.IO;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// What a project has been told about itself, and where it is kept.
/// </summary>
/// <remarks>
/// The symptom this exists for is one line: every project was handed Assets/Scripts, so a plain C#
/// project had a Unity folder created in it that had no business being there. Guessing src instead
/// would have been the same mistake with a different default, so the answer is asked once and
/// remembered, and these hold it to being asked once.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ProjectSettingsTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N"));

    public ProjectSettingsTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A scratch folder that will not delete is the operating system's problem.
        }
    }

    private ProjectSettingsService Service(TestServices services)
    {
        var settings = new ProjectSettingsService(services.Feed);
        settings.Open(_root, ProjectKind.Plain);
        return settings;
    }

    /// <summary>A project nobody has answered for is a first open.</summary>
    [Fact]
    public void AProjectWithNoSettingsIsAFirstOpen()
    {
        Assert.True(ProjectSettings.IsFirstOpen(_root));

        using var services = TestServices.Create();
        var settings = Service(services);

        Assert.True(settings.NeedsSetUp);
    }

    /// <summary>Answering once means it is never a first open again.</summary>
    [Fact]
    public void AnsweringMeansItIsNeverAskedAgain()
    {
        using var services = TestServices.Create();
        var settings = Service(services);

        settings.ScriptsFolder = "source/generated";
        settings.HasBeenSetUp = true;
        settings.Save();

        Assert.False(ProjectSettings.IsFirstOpen(_root));

        var reopened = new ProjectSettingsService(services.Feed);
        reopened.Open(_root, ProjectKind.Plain);

        Assert.False(reopened.NeedsSetUp);
        Assert.Equal("source/generated", reopened.ScriptsFolder);
    }

    /// <summary>
    /// Skipping is answering, as far as being asked again goes.
    /// </summary>
    /// <remarks>
    /// A window that reappeared until it was filled in would be a window people learn to dismiss
    /// without reading, so somebody who skipped it has decided the defaults are fine.
    /// </remarks>
    [Fact]
    public void SkippingCountsAsHavingBeenAsked()
    {
        using var services = TestServices.Create();
        var settings = Service(services);

        settings.HasBeenSetUp = true;
        settings.Save();

        Assert.False(ProjectSettings.IsFirstOpen(_root));
    }

    /// <summary>The default follows the kind rather than being one value for everything.</summary>
    [Fact]
    public void TheDefaultFolderFollowsTheKind()
    {
        Assert.Equal("Assets/Scripts", ProjectSettingsService.DefaultFolderFor(ProjectKind.Unity));
        Assert.Equal("src", ProjectSettingsService.DefaultFolderFor(ProjectKind.Plain));

        using var services = TestServices.Create();

        var plain = new ProjectSettingsService(services.Feed);
        plain.Open(_root, ProjectKind.Plain);
        Assert.Equal("src", plain.ScriptsFolder);

        var unity = new ProjectSettingsService(services.Feed);
        unity.Open(_root, ProjectKind.Unity);
        Assert.Equal("Assets/Scripts", unity.ScriptsFolder);
    }

    /// <summary>Not sharing puts everything in the local file and leaves no shared one.</summary>
    [Fact]
    public void NotSharingKeepsEverythingLocal()
    {
        using var services = TestServices.Create();
        var settings = Service(services);

        settings.ScriptsFolder = "lib";
        settings.ShareSettings = false;
        settings.HasBeenSetUp = true;
        settings.Save();

        Assert.True(File.Exists(Path.Combine(_root, ProjectSettings.LocalFileName)));
        Assert.False(File.Exists(Path.Combine(_root, ProjectSettings.SharedFileName)));

        var reopened = new ProjectSettingsService(services.Feed);
        reopened.Open(_root, ProjectKind.Plain);

        Assert.Equal("lib", reopened.ScriptsFolder);
    }

    /// <summary>
    /// Sharing splits them: conventions in the shared file, machine facts in the local one.
    /// </summary>
    /// <remarks>
    /// The whole reason there are two files. A team wants the folder and the kind in the
    /// repository; nobody wants somebody else's model path or security switch in their checkout.
    /// </remarks>
    [Fact]
    public void SharingSplitsConventionsFromMachineFacts()
    {
        using var services = TestServices.Create();
        var settings = Service(services);

        settings.ScriptsFolder = "app/generated";
        settings.Kind = ProjectKind.Plain;
        settings.DefaultModelPath = @"C:\models\something.gguf";
        settings.McpServerEnabled = true;
        settings.ShareSettings = true;
        settings.HasBeenSetUp = true;
        settings.Save();

        var shared = File.ReadAllText(Path.Combine(_root, ProjectSettings.SharedFileName));
        var local = File.ReadAllText(Path.Combine(_root, ProjectSettings.LocalFileName));

        Assert.Contains("app/generated", shared, StringComparison.Ordinal);
        Assert.Contains("Plain", shared, StringComparison.Ordinal);

        // The two things that must never be in a repository.
        Assert.DoesNotContain("something.gguf", shared, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mcpServerEnabled", shared, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("something.gguf", local, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mcpServerEnabled", local, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Turning sharing off again takes the shared file away.</summary>
    /// <remarks>
    /// Otherwise a repository would keep a file saying something the project no longer means.
    /// </remarks>
    [Fact]
    public void TurningSharingOffRemovesTheSharedFile()
    {
        using var services = TestServices.Create();
        var settings = Service(services);

        settings.ScriptsFolder = "one";
        settings.ShareSettings = true;
        settings.Save();

        Assert.True(File.Exists(Path.Combine(_root, ProjectSettings.SharedFileName)));

        settings.ShareSettings = false;
        settings.Save();

        Assert.False(File.Exists(Path.Combine(_root, ProjectSettings.SharedFileName)));

        var reopened = new ProjectSettingsService(services.Feed);
        reopened.Open(_root, ProjectKind.Plain);

        Assert.Equal("one", reopened.ScriptsFolder);
    }

    /// <summary>An existing gitignore gains the settings; a project without one is left alone.</summary>
    [Fact]
    public void OnlyAnExistingGitignoreIsTouched()
    {
        using var services = TestServices.Create();
        var settings = Service(services);

        settings.HasBeenSetUp = true;
        settings.Save();

        // No gitignore, so none was created. Deciding how somebody's repository is arranged is not
        // this application's business.
        Assert.False(File.Exists(Path.Combine(_root, ".gitignore")));

        File.WriteAllText(Path.Combine(_root, ".gitignore"), "bin/\nobj/\n");
        settings.Save();

        var ignore = File.ReadAllText(Path.Combine(_root, ".gitignore"));

        Assert.Contains(ProjectSettings.LocalFileName, ignore, StringComparison.Ordinal);
        Assert.Contains(ProjectSettings.SharedFileName, ignore, StringComparison.Ordinal);

        // Saving again does not add them twice.
        settings.Save();

        var again = File.ReadAllText(Path.Combine(_root, ".gitignore"));
        Assert.Equal(
            ignore.Split(ProjectSettings.LocalFileName).Length,
            again.Split(ProjectSettings.LocalFileName).Length);
    }

    /// <summary>A shared project keeps only the local file out of the repository.</summary>
    [Fact]
    public void SharingLeavesTheSharedFileCommittable()
    {
        using var services = TestServices.Create();
        var settings = Service(services);

        File.WriteAllText(Path.Combine(_root, ".gitignore"), "bin/\n");

        settings.ShareSettings = true;
        settings.Save();

        var ignore = File.ReadAllText(Path.Combine(_root, ".gitignore"));

        Assert.Contains(ProjectSettings.LocalFileName, ignore, StringComparison.Ordinal);
        Assert.DoesNotContain(ProjectSettings.SharedFileName + "\n", ignore, StringComparison.Ordinal);
    }

    /// <summary>
    /// An override wins over detection, everywhere that asks.
    /// </summary>
    /// <remarks>
    /// The answer decides which write rules apply, so it has to be the same answer for the index,
    /// the compiler and the output node. Detection is where they all ask.
    /// </remarks>
    [Fact]
    public void AnOverrideBeatsDetection()
    {
        // A folder that detection would call plain.
        Assert.Equal(ProjectKind.Plain, ProjectService.Detect(_root));

        using var services = TestServices.Create();
        var settings = Service(services);

        settings.Kind = ProjectKind.Unity;
        settings.ShareSettings = true;
        settings.Save();

        Assert.Equal(ProjectKind.Unity, ProjectService.Detect(_root));
    }

    /// <summary>A project with no settings detects exactly as it did before this existed.</summary>
    [Fact]
    public void WithoutSettingsDetectionIsUnchanged()
    {
        using var project = SampleProject.Create();

        Assert.Equal(ProjectKind.Unity, ProjectService.Detect(project.Root));
        Assert.Equal(ProjectKind.Plain, ProjectService.Detect(_root));
    }

    /// <summary>The folder list is what the project has, not what it might have.</summary>
    [Fact]
    public void TheFolderListIsWhatTheProjectHas()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src", "Domain"));
        Directory.CreateDirectory(Path.Combine(_root, "tests"));
        Directory.CreateDirectory(Path.Combine(_root, "obj", "Debug"));
        Directory.CreateDirectory(Path.Combine(_root, ".git", "refs"));

        using var services = TestServices.Create();
        var folders = Service(services).ExistingFolders();

        Assert.Contains("src", folders);
        Assert.Contains("src/Domain", folders);
        Assert.Contains("tests", folders);

        // Build output and tool folders are not places to put generated code.
        Assert.DoesNotContain(folders, f => f.StartsWith("obj", StringComparison.Ordinal));
        Assert.DoesNotContain(folders, f => f.StartsWith(".git", StringComparison.Ordinal));
    }

    /// <summary>A newly added Output node starts from the project's answer.</summary>
    /// <remarks>
    /// The symptom that started this. Nothing reaches into a graph already saved, because the
    /// value belongs to the node and travelled with it.
    /// </remarks>
    [Fact]
    public void ANewOutputNodeStartsFromTheProjectsAnswer()
    {
        using var services = TestServices.Create();

        var settings = Service(services);
        settings.ScriptsFolder = "app/generated";

        var factory = new App.Nodes.NodeFactory(
            new App.Services.Persistence.ModelCatalog(services.Config),
            services.Services.Mesh,
            new SilentDialogService(),
            services.Config,
            new App.Services.Extensions.ExtensionRegistry(services.Feed),
            new App.Services.Extensions.ExtensionHost(new App.Services.Processes.ChildProcessGroup(), services.Feed),
            new InMemoryCredentialStore(),
            settings);

        var node = (App.Nodes.OutputNode)factory.Create("Output");

        Assert.Equal("app/generated", node.TargetSubfolder);
    }

    /// <summary>With no project settings, a node starts where it always did.</summary>
    [Fact]
    public void WithoutAProjectTheNodeStartsWhereItAlwaysDid()
    {
        using var services = TestServices.Create();

        var node = (App.Nodes.OutputNode)services.Factory.Create("Output");

        Assert.Equal(App.Nodes.OutputNode.DefaultSubfolder, node.TargetSubfolder);
    }

    /// <summary>A settings file that will not parse is a project with no settings.</summary>
    [Fact]
    public void AnUnreadableSettingsFileIsNotAFailure()
    {
        File.WriteAllText(Path.Combine(_root, ProjectSettings.LocalFileName), "{ this is not json");

        using var services = TestServices.Create();
        var settings = new ProjectSettingsService(services.Feed);

        settings.Open(_root, ProjectKind.Plain);

        Assert.Equal("src", settings.ScriptsFolder);
        Assert.True(settings.NeedsSetUp);
    }
}
