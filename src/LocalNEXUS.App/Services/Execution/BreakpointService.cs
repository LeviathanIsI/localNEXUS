using CommunityToolkit.Mvvm.ComponentModel;
using LocalNEXUS.App.Infrastructure;
using LocalNEXUS.App.Models;

namespace LocalNEXUS.App.Services.Execution;

/// <summary>
/// Holds a run at the wires somebody marked, and hands back whatever they released.
/// </summary>
/// <remarks>
/// The counterpart to the pause button rather than a second copy of it. Pausing stops the run
/// between nodes and asks nothing; a breakpoint stops it on one wire and shows the thing that was
/// about to travel down it, which is the only place a value can be read before anything has acted
/// on it.
///
/// Nothing here knows what a node is. It is handed a connection and a value, which are graph
/// facts, and it does the same thing whatever produced them.
/// </remarks>
public sealed partial class BreakpointService : ObservableObject
{
    private readonly IActivityFeed _feed;

    /// <summary>The stop being held right now, or null when nothing is held.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHolding))]
    private BreakpointStop? _current;

    public BreakpointService(IActivityFeed feed) => _feed = feed;

    /// <summary>True while a run is held at a breakpoint.</summary>
    public bool IsHolding => Current is not null;

    /// <summary>
    /// Holds until somebody releases, and returns the value to carry on with.
    /// </summary>
    /// <remarks>
    /// Cancelling releases with the value untouched rather than leaving the task hanging, because
    /// a run being stopped has to unwind through the same path as one being let go.
    /// </remarks>
    public async Task<object?> HoldAsync(Connection connection, object? value, CancellationToken ct)
    {
        var stop = new BreakpointStop(connection, value);

        Current = stop;

        _feed.Add(
            ActivityKind.Confirmation,
            $"Stopped on the wire from {connection.Source.Owner.Title}",
            $"{stop.Where}. Edit what is passing, or release it.");

        try
        {
            await using var registration = ct.Register(stop.Abandon);
            return await stop.Released.ConfigureAwait(false);
        }
        finally
        {
            Current = null;
        }
    }

    /// <summary>Releases anything held, so a run that is being stopped can unwind.</summary>
    public void ReleaseAll() => Current?.Abandon();
}
