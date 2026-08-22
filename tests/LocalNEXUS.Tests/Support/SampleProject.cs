using System.IO;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// A throwaway C# project on disk, shaped to exercise the things being tested.
/// </summary>
/// <remarks>
/// Generated per test rather than checked in, because a fixture that lives in the repository is a
/// fixture somebody edits for one test and breaks for six others. It is deleted on dispose.
///
/// It never goes near the repository, a real Unity project, or anywhere the application keeps its
/// own data. A test that writes files has to be told exactly where, or the first mistake in it
/// costs somebody their work.
/// </remarks>
public sealed class SampleProject : IDisposable
{
    private SampleProject(string root)
    {
        Root = root;
    }

    /// <summary>The project folder, which stands in for a Unity project root.</summary>
    public string Root { get; }

    /// <summary>Where scripts live, as Unity arranges them.</summary>
    public string Scripts => Path.Combine(Root, "Assets", "Scripts");

    /// <summary>Builds one, with enough shape to be worth pointing anything at.</summary>
    public static SampleProject Create()
    {
        // Under the system temp folder and nowhere else. Named so that anything left behind by a
        // crashed run is obviously ours and obviously disposable.
        var root = Path.Combine(Path.GetTempPath(), "localnexus-tests", Guid.NewGuid().ToString("N"));
        var project = new SampleProject(root);

        Directory.CreateDirectory(project.Scripts);
        Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));

        // Enough for the locator to believe this is a Unity project without one being installed.
        File.WriteAllText(
            Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"),
            "m_EditorVersion: 2022.3.20f1" + Environment.NewLine);

        // An interface and its implementation, so dependency ordering has something to order.
        project.Write("IDamageable.cs", """
            namespace Game
            {
                public interface IDamageable
                {
                    void TakeDamage(int amount);
                }
            }
            """);

        project.Write("Health.cs", """
            namespace Game
            {
                public class Health : IDamageable
                {
                    private int _current;

                    public int Current => _current;

                    public void TakeDamage(int amount)
                    {
                        _current -= amount;
                    }
                }
            }
            """);

        // A plain type a duplicate guard should refuse a second copy of.
        project.Write("InventorySlot.cs", """
            namespace Game
            {
                public class InventorySlot
                {
                    public string ItemId;
                    public int Count;
                }
            }
            """);

        // A Unity shaped script: the file name matches the class, it derives from MonoBehaviour,
        // it has a serialized field, and it has the meta sibling Unity binds scenes through.
        project.Write("Spinner.cs", """
            using UnityEngine;

            namespace Game
            {
                public class Spinner : MonoBehaviour
                {
                    [SerializeField]
                    private float speed = 90f;

                    private void Update()
                    {
                        transform.Rotate(0f, speed * Time.deltaTime, 0f);
                    }
                }
            }
            """);

        File.WriteAllText(
            Path.Combine(project.Scripts, "Spinner.cs.meta"),
            "fileFormatVersion: 2" + Environment.NewLine + "guid: 0123456789abcdef0123456789abcdef" + Environment.NewLine);

        return project;
    }

    /// <summary>Writes a script into the project and returns its full path.</summary>
    public string Write(string fileName, string content)
    {
        var path = Path.Combine(Scripts, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.ReplaceLineEndings());
        return path;
    }

    /// <summary>Reads a script back.</summary>
    public string Read(string fileName) => File.ReadAllText(Path.Combine(Scripts, fileName));

    /// <summary>True when a script is on disk.</summary>
    public bool Exists(string fileName) => File.Exists(Path.Combine(Scripts, fileName));

    /// <summary>The path a script would have, whether or not it is there.</summary>
    public string PathTo(string fileName) => Path.Combine(Scripts, fileName);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A temp folder that will not delete is not a test failure. It is under the system
            // temp folder and will go with everything else there.
        }
    }
}
