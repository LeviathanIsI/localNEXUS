using System.IO;

namespace LocalNEXUS.App.Services.Persistence;

/// <summary>
/// The user editable list of extra folders scanned for models.
/// </summary>
/// <remarks>
/// A plain text file, one folder per line, because the thing it replaces is a person keeping
/// their models on a second drive and wanting the application to look there. Format does not
/// appear in it: a listed folder is scanned for GGUF files and safetensors model folders alike,
/// so nobody has to say in advance what kind of models they keep where.
///
/// It sits alongside the folders added through the settings panel rather than replacing them.
/// The panel writes configuration; this file is written by hand, and both are read.
/// </remarks>
public static class ModelPathsFile
{
    private const string Header = """
        # Extra folders LocalNEXUS scans for models, one per line.
        #
        # Both formats are found in any listed folder, and subfolders are searched:
        #   a .gguf file is a model, and a folder holding config.json beside .safetensors
        #   weight files is a model. Nothing here says which is which.
        #
        # Lines starting with # are ignored. Blank lines are ignored. Environment variables
        # are expanded, so %USERPROFILE%\models works.
        #
        # Examples:
        #   D:\models
        #   %USERPROFILE%\.cache\huggingface\hub

        """;

    /// <summary>
    /// Reads the folders listed in the file. A missing or unreadable file yields nothing rather
    /// than an error, because a broken line in it must never stop the application from starting.
    /// </summary>
    public static IReadOnlyList<string> Read()
    {
        if (!File.Exists(AppPaths.ModelPathsFile))
        {
            return Array.Empty<string>();
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(AppPaths.ModelPathsFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        var folders = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                continue;
            }

            var expanded = Environment.ExpandEnvironmentVariables(trimmed).Trim('"');

            try
            {
                folders.Add(Path.GetFullPath(expanded));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // A line that is not a path is the user's typo to fix. The catalogue reports how
                // many folders it searched, which is where a missing one shows up.
            }
        }

        return folders;
    }

    /// <summary>
    /// Writes the commented template if the file is not there yet, so a first run leaves
    /// something to edit rather than something to invent.
    /// </summary>
    public static void EnsureCreated()
    {
        if (File.Exists(AppPaths.ModelPathsFile))
        {
            return;
        }

        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(AppPaths.ModelPathsFile, Header);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to write the template is not worth failing startup over. The file
            // is optional, and every other search folder still works.
        }
    }
}
