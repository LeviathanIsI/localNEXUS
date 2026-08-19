using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// The handful of settings that survive between sessions. There is no settings screen in this
/// slice; the file simply remembers what the user last chose through the File menu.
/// </summary>
public sealed class AppConfig
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The Unity project folder that was open when the app last closed.</summary>
    public string? LastUnityProjectPath { get; set; }

    /// <summary>Folders added by the user that are scanned for GGUF files alongside the default one.</summary>
    public List<string> ExtraModelFolders { get; set; } = new();

    /// <summary>The graph file that was last saved or loaded.</summary>
    public string? LastGraphPath { get; set; }

    /// <summary>
    /// This install's own identity, generated once and never regenerated. A running mesh node
    /// has a stronger identity of its own, its public key, which is what peers and any later
    /// reputation attach to; this one exists so an install still has a stable id of its own
    /// before its node has ever been started.
    /// </summary>
    public Guid SourceId { get; set; }

    /// <summary>Whether the mesh node is started with the application.</summary>
    public bool MeshEnabled { get; set; }

    /// <summary>Whether this machine offers its own compute to the mesh rather than only routing.</summary>
    public bool MeshContribute { get; set; }

    /// <summary>The GGUF this machine serves while contributing. Blank offers capacity without a model.</summary>
    public string? MeshOfferedModelPath { get; set; }

    /// <summary>
    /// Cap on the memory this machine offers, in GB. Zero lets the engine decide. Unlike the
    /// declared offer the previous engine took on trust, this one is enforced by the planner.
    /// </summary>
    public double MeshMaxVramGb { get; set; }

    /// <summary>Invite token of a mesh to join. Blank means this install hosts its own private mesh.</summary>
    public string? MeshJoinToken { get; set; }

    /// <summary>Friendly name of the mesh this install hosts.</summary>
    public string? MeshName { get; set; }

    /// <summary>
    /// Advertises this mesh for public discovery. Off by default: a private mesh on the local
    /// network is the default posture, and this is the only setting that changes it.
    /// </summary>
    public bool MeshPublish { get; set; }

    /// <summary>Port the mesh node's OpenAI compatible API listens on.</summary>
    public int MeshApiPort { get; set; } = 9337;

    /// <summary>Port the mesh node's management API answers on.</summary>
    public int MeshConsolePort { get; set; } = 3131;

    /// <summary>
    /// Reads the configuration from disk. A missing or unreadable file yields defaults rather
    /// than an error, because losing this state is never worth blocking startup over.
    /// </summary>
    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(AppPaths.ConfigFile))
            {
                return new AppConfig();
            }

            var json = File.ReadAllText(AppPaths.ConfigFile);
            return JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AppConfig();
        }
    }

    /// <summary>
    /// Reads the configuration, writing a default file when there is not one yet, so that a first
    /// run leaves a complete and editable data folder behind.
    /// </summary>
    public static AppConfig LoadOrCreate()
    {
        var existed = File.Exists(AppPaths.ConfigFile);
        var config = Load();

        if (!existed)
        {
            config.Save();
        }

        return config;
    }

    /// <summary>Writes the configuration to disk, creating the data folder if needed.</summary>
    public void Save()
    {
        AppPaths.EnsureCreated();
        var json = JsonSerializer.Serialize(this, SerializerOptions);
        File.WriteAllText(AppPaths.ConfigFile, json);
    }
}
