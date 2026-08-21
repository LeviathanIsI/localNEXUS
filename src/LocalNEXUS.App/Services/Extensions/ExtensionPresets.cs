using System.IO;
using LocalNEXUS.App.Models.Extensions;

namespace LocalNEXUS.App.Services.Extensions;

/// <summary>
/// The curated extensions this application knows how to install.
/// </summary>
/// <remarks>
/// Deliberately a short hand written list rather than a browsable directory. A directory becomes
/// a maintenance job the day somebody's server moves, and a stale entry in a browsable list is
/// worse than no list at all because it looks authoritative.
/// <para>
/// Nothing here is preinstalled. Presets appear as available and are installed on request,
/// because the install path is the thing that has to work, and shipping something already
/// installed means that path is never exercised until it fails for somebody else.
/// </para>
/// </remarks>
public static class ExtensionPresets
{
    /// <summary>Every preset, in the order the panel lists them.</summary>
    public static IReadOnlyList<ExtensionManifest> All { get; } = new[]
    {
        AnkleBreaker(),
        UnityOfficial()
    };

    /// <summary>Finds a preset by id.</summary>
    public static ExtensionManifest? Find(string id)
        => All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    private static ExtensionManifest AnkleBreaker() => new(
        Id: "studio.anklebreaker.unity-mcp",
        Name: "AnkleBreaker Unity MCP",
        Version: "latest",
        Description:
            "Drives the Unity editor from a model: scenes, assets, components, the console and the " +
            "project. The broadest of the Unity servers, with several hundred tools depending on " +
            "the version it resolves to.",
        Author: "AnkleBreaker Studio",
        Homepage: "https://github.com/AnkleBreaker-Studio/unity-mcp-plugin",
        Contracts: new[] { ExtensionContract.Mcp },
        Tools: Array.Empty<ToolContribution>(),
        Nodes: Array.Empty<NodeContribution>(),
        Prerequisites: new[]
        {
            new ExtensionPrerequisite(
                PrerequisiteKind.Executable,
                "node",
                "The server is an npm package, so Node runs it.",
                InstallCommand: "winget",
                InstallArguments: new[]
                {
                    "install", "--id", "OpenJS.NodeJS.LTS", "--exact",
                    "--silent", "--accept-package-agreements", "--accept-source-agreements"
                }),
            new ExtensionPrerequisite(
                PrerequisiteKind.UnityPackage,
                "com.anklebreaker.unity-mcp",
                "The editor side of this server is a Unity package, and without it the server has nothing to talk to.")
        },
        // The package runner rather than a global install, so the version resolves per run and
        // nothing is added to the machine that this application did not ask for.
        Launch: new ExtensionLaunch("npx", new[] { "--yes", "anklebreaker-unity-mcp@latest" }));

    private static ExtensionManifest UnityOfficial()
    {
        var relay = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".unity", "relay", "relay_win.exe");

        return new ExtensionManifest(
            Id: "com.unity.ai.assistant.mcp",
            Name: "Unity MCP server",
            Version: "bundled with the editor",
            Description:
                "Unity's own server, which reaches the running editor over a named pipe. Narrower " +
                "than the community servers and tied to the AI Assistant package.",
            Author: "Unity Technologies",
            Homepage: "https://docs.unity3d.com/Packages/com.unity.ai.assistant@latest",
            Contracts: new[] { ExtensionContract.Mcp },
            Tools: Array.Empty<ToolContribution>(),
            Nodes: Array.Empty<NodeContribution>(),
            Prerequisites: new[]
            {
                new ExtensionPrerequisite(
                    PrerequisiteKind.Executable,
                    relay,
                    "The relay binary ships with the editor and is what speaks MCP."),
                new ExtensionPrerequisite(
                    PrerequisiteKind.UnityPackage,
                    "com.unity.ai.assistant",
                    "The relay talks to this package inside the editor. Without it there is nothing on the other end."),
                new ExtensionPrerequisite(
                    PrerequisiteKind.UnityEditor,
                    "Unity editor",
                    "The relay connects to a running editor over a named pipe, so the project has to be open in Unity.")
            },
            Launch: new ExtensionLaunch(relay, new[] { "--mcp" }),
            // Said out loud rather than discovered later. Unity has deprecated this in favour of
            // their command line tool, and the current AI Assistant package caps connected MCP
            // clients at zero, which is a deliberate block on third party clients rather than a
            // bug. It is offered because the relay still exists and still works where the older
            // package is installed, and it is labelled because installing it on a current project
            // would otherwise look like a bug in this application.
            Deprecated:
                "Unity has deprecated their MCP server in favour of the Unity CLI, and recent " +
                "versions of the AI Assistant package refuse third party clients outright. Expect " +
                "this to connect only on projects using an older package.");
    }
}
