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
        UnityOfficial(),
        OpenSpec()
    };

    /// <summary>Finds a preset by id.</summary>
    public static ExtensionManifest? Find(string id)
        => All.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// OpenSpec, which is the only preset that brings a tab with it.
    /// </summary>
    /// <remarks>
    /// Not installed by default, like the other two, so the install path and its prerequisite check
    /// are exercised rather than assumed. Node is the prerequisite and the existing check handles
    /// it: it says what is missing, offers to install it, and installs nothing if that is declined.
    ///
    /// The launch is the package runner rather than a global install, so the version resolves per
    /// run and nothing is added to the machine this application did not ask for. It fetches the
    /// bridge on install like every other extension, so OpenSpec's licence stays its own and its
    /// updates are its own; nothing about it is bundled here.
    /// </remarks>
    private static ExtensionManifest OpenSpec() => new(
        Id: "ai.fission.openspec",
        Name: "OpenSpec",
        Version: "latest",
        Description:
            "Adds a Spec tab to the window for spec driven planning. Lists your changes, shows " +
            "each one's proposal, specs, design and tasks with whether each is done, ready or " +
            "blocked, and sends a change's task list to the Workspace as a request for the graph " +
            "to implement.",
        Author: "Fission AI",
        Homepage: "https://github.com/Fission-AI/OpenSpec",
        Contracts: new[] { ExtensionContract.Spec },
        Tools: Array.Empty<ToolContribution>(),
        Nodes: Array.Empty<NodeContribution>(),
        Prerequisites: new[]
        {
            new ExtensionPrerequisite(
                PrerequisiteKind.Executable,
                "node",
                "OpenSpec is an npm package, so Node runs it.",
                InstallCommand: "winget",
                InstallArguments: new[]
                {
                    "install", "--id", "OpenJS.NodeJS.LTS", "--exact",
                    "--silent", "--accept-package-agreements", "--accept-source-agreements"
                })
        },
        Launch: new ExtensionLaunch("npx", new[] { "--yes", "@fission-ai/openspec-bridge@latest" }));

    private static ExtensionManifest AnkleBreaker() => new(
        Id: "studio.anklebreaker.unity-mcp",
        Name: "AnkleBreaker Unity MCP",
        Version: "latest",
        Description:
            "Lets a model work the editor for you: scenes, assets, components, the console and the " +
            "project. The broadest of the Unity servers, with several hundred tools.",
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
                "Unity's own server. Fewer tools than the community ones and tied to the AI " +
                "Assistant package, but it comes from Unity.",
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
            // Said out loud rather than discovered later, and said accurately, which takes more
            // words than the previous version used.
            //
            // The server still ships and is still documented. Unity has deprecated it in favour
            // of their command line tool. From AI Assistant 2.7.0 a local licence entitlement
            // counts against the connection limit, and a free licence therefore sees a limit of
            // zero and gets a connection revoked.
            //
            // Whether that is intended is genuinely unclear and this does not pretend otherwise.
            // It is on Unity's issue tracker as reproducible on 2.7.0 and not on 2.6.0, which
            // reads like a regression, while the changelog line about entitlements reads like
            // intent. Both are true at once and neither settles it.
            //
            // It is offered because it still works where an older package is installed, and it is
            // labelled because otherwise a failure to connect on a current project looks like a
            // bug in this application.
            Deprecated:
                "Unity has deprecated this in favour of the Unity CLI, though the server still " +
                "ships and is still documented. From AI Assistant 2.7.0 your local licence " +
                "entitlement counts against the connection limit, so a free licence sees a limit " +
                "of zero and gets 'Connection revoked'. Whether that is deliberate is unclear: it " +
                "is filed on Unity's issue tracker as happening on 2.7.0 and not 2.6.0, while the " +
                "changelog reads like it was intended. Expect this to connect on an older package " +
                "or a paid licence, and not otherwise.");
    }
}
