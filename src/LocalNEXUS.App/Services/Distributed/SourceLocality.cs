namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Where a source sits relative to this install.
/// </summary>
/// <remarks>
/// A routing attribute only. It orders candidates (nearer first) and labels the UI, and it must
/// never be branched on for correctness: a section is covered by whichever source fills it,
/// wherever that source happens to be.
/// </remarks>
public enum SourceLocality
{
    /// <summary>The machine this install is running on.</summary>
    ThisMachine,

    /// <summary>A machine reachable on the local network.</summary>
    LocalNetwork,

    /// <summary>A machine reached over the internet.</summary>
    Remote
}
