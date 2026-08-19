using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// What a source can serve. Which model sections a source can cover is derived from these
/// numbers by the coverage planner rather than stored, so capability edits take effect on the
/// next plan without any bookkeeping.
/// </summary>
public sealed partial class SourceCapabilities : ObservableObject
{
    /// <summary>
    /// Memory the source can devote to model sections, in MiB. Zero means unknown, in which
    /// case the planner treats the source as an unweighted candidate rather than ruling it out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private long _memoryMb;

    /// <summary>One line for the panel.</summary>
    public string Summary => MemoryMb > 0 ? $"{MemoryMb} MiB" : "memory unknown";
}
