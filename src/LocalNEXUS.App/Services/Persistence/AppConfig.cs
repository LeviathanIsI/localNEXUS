using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalNEXUS.App.Services.Distributed;

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
    /// This install's stable source identity. Generated once on first use of the source
    /// registry and never regenerated, because reputation will attach to it later.
    /// </summary>
    public Guid SourceId { get; set; }

    /// <summary>Sources registered by the user, hydrated by the registry at startup.</summary>
    public List<KnownSourceRecord> KnownSources { get; set; } = new();

    /// <summary>
    /// Declared memory of this machine in MiB. Zero means detect automatically at startup.
    /// </summary>
    public long ThisMachineMemoryMb { get; set; }

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
