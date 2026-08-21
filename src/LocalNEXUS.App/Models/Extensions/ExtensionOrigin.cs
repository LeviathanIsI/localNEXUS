namespace LocalNEXUS.App.Models.Extensions;

/// <summary>
/// Where an extension came from, which is the first thing worth knowing when one misbehaves.
/// </summary>
public enum ExtensionOrigin
{
    /// <summary>One of the curated entries this application ships knowledge of.</summary>
    Preset,

    /// <summary>An npm package, run through the package runner rather than installed globally.</summary>
    Npm,

    /// <summary>A git repository, cloned and launched per the manifest it carries.</summary>
    Git,

    /// <summary>A folder on this machine holding a manifest.</summary>
    Disk,

    /// <summary>A command line given directly. The escape hatch that makes anything possible.</summary>
    Command
}
