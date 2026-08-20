namespace LocalNEXUS.App.ViewModels;

/// <summary>
/// The tabs of the bottom panel, in the order they are shown.
/// </summary>
/// <remarks>
/// Three tabs answering three different questions. Problems is what is wrong with the code right
/// now, Activity is what happened during the run, and Output is what the engines themselves
/// printed. Collapsing any two of them into one loses the reason someone opened it.
/// </remarks>
public enum BottomPanelTab
{
    /// <summary>Compiler diagnostics from the compile check nodes in the graph.</summary>
    Problems,

    /// <summary>The streaming run transcript.</summary>
    Activity,

    /// <summary>Raw logs from the engines and from the application itself.</summary>
    Output
}
