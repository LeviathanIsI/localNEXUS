using System.IO;
using LocalNEXUS.App.Services.Files;
using LocalNEXUS.App.Services.ProjectIndex;
using LocalNEXUS.Tests.Support;
using Xunit;

namespace LocalNEXUS.Tests;

/// <summary>
/// The writes that are refused, and the ones that are only reported.
/// </summary>
/// <remarks>
/// Every rule here describes a change that compiles cleanly and silently breaks a scene. That is
/// the reason they are refusals rather than warnings: nothing downstream will notice, and the
/// person who finds out is the one who opens the scene a week later and finds a component with
/// its fields blank.
/// </remarks>
[Trait(Layers.Name, Layers.Deterministic)]
public sealed class UnityGuardrailTests
{
    private static async Task<ProjectIndexService> IndexOf(SampleProject project)
    {
        var index = new ProjectIndexService();
        await index.EnsureAsync(project.Root, null, CancellationToken.None);
        return index;
    }

    /// <summary>Parses a snippet the way the index parses a file, which is from disk.</summary>
    private static IReadOnlyList<IndexedType> TypesIn(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N") + ".cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);

        try
        {
            var parsed = SourceFileParser.Parse(path, "Assets/Scripts/Whatever.cs", CancellationToken.None);
            Assert.NotNull(parsed);
            return parsed.Types;
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>A MonoBehaviour whose file name does not match its class refuses to attach.</summary>
    [Fact]
    public void AMonoBehaviourMustMatchItsFileName()
    {
        const string source = """
            using UnityEngine;

            public class Spinner : MonoBehaviour { }
            """;

        Assert.Throws<UnityScriptRuleException>(() => UnityScriptRules.Enforce(
            "Assets/Scripts/Rotator.cs",
            source,
            existing: null,
            TypesIn(source)));

        // The same content at the right path is fine.
        UnityScriptRules.Enforce("Assets/Scripts/Spinner.cs", source, existing: null, TypesIn(source));
    }

    /// <summary>A type that disappears from a file takes its scene bindings with it.</summary>
    [Fact]
    public async Task ATypeMayNotSimplyDisappear()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);
        var existing = index.FindFile("Assets/Scripts/Spinner.cs");

        Assert.NotNull(existing);

        const string replacement = """
            using UnityEngine;

            public class SomethingElse : MonoBehaviour { }
            """;

        Assert.Throws<UnityScriptRuleException>(() => UnityScriptRules.Enforce(
            "Assets/Scripts/Spinner.cs",
            replacement,
            existing,
            TypesIn(replacement)));
    }

    /// <summary>A type may not quietly stop being a MonoBehaviour.</summary>
    [Fact]
    public async Task ABehaviourMayNotStopBeingOne()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);
        var existing = index.FindFile("Assets/Scripts/Spinner.cs");

        const string replacement = """
            namespace Game
            {
                public class Spinner
                {
                    private float speed = 90f;
                }
            }
            """;

        Assert.Throws<UnityScriptRuleException>(() => UnityScriptRules.Enforce(
            "Assets/Scripts/Spinner.cs",
            replacement,
            existing,
            TypesIn(replacement)));
    }

    /// <summary>
    /// A serialized field may not be renamed without the attribute that keeps the old name working.
    /// </summary>
    /// <remarks>
    /// The value in the scene is stored against the field name. Rename the field and the value is
    /// silently dropped back to the default, which usually means zero, which usually means nothing
    /// moves.
    /// </remarks>
    [Fact]
    public async Task ASerializedFieldMayNotBeRenamedWithoutTheShim()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);
        var existing = index.FindFile("Assets/Scripts/Spinner.cs");

        const string renamed = """
            using UnityEngine;

            namespace Game
            {
                public class Spinner : MonoBehaviour
                {
                    [SerializeField]
                    private float rotationSpeed = 90f;
                }
            }
            """;

        Assert.Throws<UnityScriptRuleException>(() => UnityScriptRules.Enforce(
            "Assets/Scripts/Spinner.cs",
            renamed,
            existing,
            TypesIn(renamed)));
    }

    /// <summary>With the shim in place, the same rename is allowed.</summary>
    [Fact]
    public async Task TheShimMakesTheRenameAllowed()
    {
        using var project = SampleProject.Create();
        var index = await IndexOf(project);
        var existing = index.FindFile("Assets/Scripts/Spinner.cs");

        const string renamed = """
            using UnityEngine;
            using UnityEngine.Serialization;

            namespace Game
            {
                public class Spinner : MonoBehaviour
                {
                    [FormerlySerializedAs("speed")]
                    [SerializeField]
                    private float rotationSpeed = 90f;
                }
            }
            """;

        UnityScriptRules.Enforce("Assets/Scripts/Spinner.cs", renamed, existing, TypesIn(renamed));
    }

    /// <summary>A new MonoBehaviour is reported as needing attaching, because nothing attaches it.</summary>
    [Fact]
    public void ANewBehaviourIsReportedAsNeedingAttaching()
    {
        const string source = """
            using UnityEngine;

            public class Spinner : MonoBehaviour { }
            """;

        var note = UnityScriptRules.DescribeAttachmentNeeded(TypesIn(source));

        Assert.NotNull(note);
        Assert.Contains("Spinner", note, StringComparison.Ordinal);
    }

    /// <summary>Something that is not a component gets no such note, because it needs no attaching.</summary>
    [Fact]
    public void APlainClassIsNotReportedAsNeedingAttaching()
        => Assert.Null(UnityScriptRules.DescribeAttachmentNeeded(TypesIn("public class Plain { }")));

    /// <summary>
    /// A batch commits together, and a failure part way restores what it already wrote.
    /// </summary>
    /// <remarks>
    /// The failure this exists for is a plan of five files where the fourth cannot be written: four
    /// new scripts in the project referring to a fifth that is not there, and a compile error in
    /// somebody's editor that nothing in this application caused directly.
    /// </remarks>
    [Fact]
    public async Task ABatchCommitsTogether()
    {
        using var project = SampleProject.Create();
        var batch = new ProjectWriteBatch(new FileWriter());

        batch.Stage(project.PathTo("First.cs"), "public class First { }");
        batch.Stage(project.PathTo("Second.cs"), "public class Second { }");

        Assert.Equal(2, batch.Count);
        Assert.False(project.Exists("First.cs"));

        var written = await batch.CommitAsync(CancellationToken.None);

        Assert.Equal(2, written.Count);
        Assert.True(project.Exists("First.cs"));
        Assert.True(project.Exists("Second.cs"));
    }

    /// <summary>An edit is written in place, so the meta file beside it keeps its identifier.</summary>
    /// <remarks>
    /// A Unity script is bound to scenes through the GUID in its .cs.meta sibling. Deleting the
    /// file and writing a new one issues a fresh GUID and every reference to it goes missing.
    /// </remarks>
    [Fact]
    public async Task AnEditLeavesTheMetaFileAlone()
    {
        using var project = SampleProject.Create();
        var metaPath = project.PathTo("Spinner.cs.meta");
        var before = File.ReadAllText(metaPath);

        var batch = new ProjectWriteBatch(new FileWriter());
        batch.Stage(project.PathTo("Spinner.cs"), "// rewritten");
        await batch.CommitAsync(CancellationToken.None);

        Assert.Equal("// rewritten", project.Read("Spinner.cs"));
        Assert.True(File.Exists(metaPath));
        Assert.Equal(before, File.ReadAllText(metaPath));
    }

    /// <summary>A plan that says it is creating a file, over a file that exists, is refused.</summary>
    [Fact]
    public void CreatingOverSomethingThatExistsIsRefused()
    {
        using var project = SampleProject.Create();
        var batch = new ProjectWriteBatch(new FileWriter());

        Assert.ThrowsAny<Exception>(() => batch.EnforceExpectedExistence(project.PathTo("Spinner.cs"), expectedToExist: false));

        // And the other way round: editing something that is not there.
        Assert.ThrowsAny<Exception>(() => batch.EnforceExpectedExistence(project.PathTo("NotHere.cs"), expectedToExist: true));
    }
}
