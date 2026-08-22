namespace LocalNEXUS.Tests.Support;

/// <summary>
/// Which layer a test belongs to, so the two can be run apart.
/// </summary>
/// <remarks>
/// One layer is arithmetic and stubs and belongs on every build. The other loads several gigabytes
/// of model and takes minutes, and belongs where somebody has asked for it. Running them together
/// by default would mean the fast one stops being run.
///
/// Select with <c>dotnet test --filter Layer=Deterministic</c> or <c>Layer=EndToEnd</c>.
/// </remarks>
public static class Layers
{
    /// <summary>The trait name both layers are tagged with.</summary>
    public const string Name = "Layer";

    /// <summary>Stubs and pure functions. Fast, and it fails only when the application is wrong.</summary>
    public const string Deterministic = "Deterministic";

    /// <summary>A real model on real hardware. Slow, and it needs one to be present.</summary>
    public const string EndToEnd = "EndToEnd";
}
