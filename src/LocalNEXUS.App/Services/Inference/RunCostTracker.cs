using CommunityToolkit.Mvvm.ComponentModel;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// What the current run has spent so far.
/// </summary>
/// <remarks>
/// One instance for the application, reset when a run starts. Every cloud call adds to it and
/// nothing local touches it at all, so a graph that never leaves this machine shows no cost
/// rather than showing a confident zero, which would imply the number was measured.
/// </remarks>
public sealed partial class RunCostTracker : ObservableObject
{
    /// <summary>What the run has spent.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(HasCost))]
    private decimal _total;

    /// <summary>How many cloud calls have been priced.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    private int _calls;

    /// <summary>True once something in this run has actually cost money.</summary>
    public bool HasCost => Calls > 0;

    /// <summary>The running figure, for the feed and the status bar.</summary>
    public string Summary => Calls == 0
        ? string.Empty
        : $"{RunCost.Format(Total)} so far";

    /// <summary>Clears the total. Called when a run begins.</summary>
    public void Reset()
    {
        Total = 0m;
        Calls = 0;
    }

    /// <summary>
    /// Adds what one call cost, and returns the figure for that call.
    /// </summary>
    /// <returns>The cost of this call, or null when the provider has no rates to price it with.</returns>
    public decimal? Add(CloudProvider? provider, int? promptTokens, int? completionTokens)
    {
        if (!RunCost.HasRates(provider))
        {
            // A local model, or a provider whose rates nobody has filled in. Saying nothing is
            // the honest answer; a zero would read as measured.
            return null;
        }

        var cost = RunCost.Actual(provider!, promptTokens ?? 0, completionTokens ?? 0);

        Total += cost;
        Calls++;

        return cost;
    }
}
