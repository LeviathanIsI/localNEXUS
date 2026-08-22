using System.IO;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// What a folder is taken to be, which decides whether the Unity rules are in force.
/// </summary>
/// <remarks>
/// Detection is asked to be right in both directions and the cost of the two mistakes is not the
/// same. Calling a Unity project plain switches off refusals that exist because the edit they
/// refuse compiles cleanly and breaks a scene, and nothing else in the application would catch it.
/// Calling a plain project Unity demands Unity attributes from a project that has no Unity in it.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class ProjectKindTests
{
    /// <summary>A scratch folder, torn down with the test.</summary>
    private sealed class Folder : IDisposable
    {
        public Folder()
        {
            Root = Path.Combine(Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public Folder WithDirectory(string relative)
        {
            Directory.CreateDirectory(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
            return this;
        }

        public Folder WithFile(string relative, string content = "x")
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return this;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
                // A scratch folder that will not delete is the operating system's problem, not the
                // test's, and failing here would blame the test for it.
            }
        }
    }

    /// <summary>The version file is written by the editor and by nothing else.</summary>
    [Fact]
    public void AProjectVersionFileIsEnough()
    {
        using var folder = new Folder().WithFile("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 2022.3.20f1");

        Assert.Equal(ProjectKind.Unity, ProjectService.Detect(folder.Root));
    }

    /// <summary>Assets beside ProjectSettings is a Unity project even before the editor has run.</summary>
    [Fact]
    public void AssetsBesideProjectSettingsIsUnity()
    {
        using var folder = new Folder().WithDirectory("Assets").WithDirectory("ProjectSettings");

        Assert.Equal(ProjectKind.Unity, ProjectService.Detect(folder.Root));
    }

    /// <summary>So is Assets beside a package manifest.</summary>
    [Fact]
    public void AssetsBesideAPackageManifestIsUnity()
    {
        using var folder = new Folder().WithDirectory("Assets").WithFile("Packages/manifest.json", "{}");

        Assert.Equal(ProjectKind.Unity, ProjectService.Detect(folder.Root));
    }

    /// <summary>
    /// An Assets folder on its own is not enough.
    /// </summary>
    /// <remarks>
    /// The rule this replaces. Plenty of projects that have never seen Unity keep their images and
    /// stylesheets in a folder called Assets, and every one of them would have been handed Unity
    /// refusals.
    /// </remarks>
    [Fact]
    public void AnAssetsFolderAloneIsNotUnity()
    {
        using var folder = new Folder().WithDirectory("Assets").WithFile("Assets/site.css", "body { }");

        Assert.Equal(ProjectKind.Plain, ProjectService.Detect(folder.Root));
    }

    /// <summary>An ordinary C# project is plain, which is an answer rather than a failure.</summary>
    [Fact]
    public void AnOrdinaryProjectIsPlain()
    {
        using var folder = new Folder()
            .WithFile("Library.csproj", "<Project />")
            .WithFile("src/Thing.cs", "public class Thing { }");

        Assert.Equal(ProjectKind.Plain, ProjectService.Detect(folder.Root));
    }

    /// <summary>Nothing open is neither.</summary>
    [Fact]
    public void NothingOpenIsNone()
    {
        Assert.Equal(ProjectKind.None, ProjectService.Detect(null));
        Assert.Equal(ProjectKind.None, ProjectService.Detect("   "));
        Assert.Equal(ProjectKind.None, ProjectService.Detect(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    /// <summary>Opening a folder records what it is, and closing forgets it.</summary>
    [Fact]
    public void OpeningRecordsTheKindAndClosingForgetsIt()
    {
        using var folder = new Folder().WithFile("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.0.1f1");

        var service = new ProjectService();
        service.Open(folder.Root);

        Assert.True(service.IsUnity);
        Assert.Equal("Unity project", service.KindText);
        Assert.Contains("Unity write rules are in force", service.StatusText, StringComparison.Ordinal);

        service.Close();

        Assert.Equal(ProjectKind.None, service.Kind);
        Assert.False(service.IsUnity);
    }

    /// <summary>A plain project says so rather than saying something is missing.</summary>
    [Fact]
    public void APlainProjectSaysWhatItIs()
    {
        using var folder = new Folder().WithFile("src/Thing.cs", "public class Thing { }");

        var service = new ProjectService();
        service.Open(folder.Root);

        Assert.False(service.IsUnity);
        Assert.Equal("C# project", service.KindText);
        Assert.Contains("do not apply", service.StatusText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The project the tests and the evaluation build is detected as Unity.
    /// </summary>
    /// <remarks>
    /// The load bearing one. Every guardrail test and the whole twenty task evaluation run against
    /// this shape, so if it were ever read as plain they would all quietly stop testing what they
    /// claim to and start passing for the wrong reason.
    /// </remarks>
    [Fact]
    public void TheSampleProjectIsAUnityProject()
    {
        using var project = SampleProject.Create();

        Assert.Equal(ProjectKind.Unity, ProjectService.Detect(project.Root));
    }
}
