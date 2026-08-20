namespace LocalNEXUS.App.Services.Python;

/// <summary>
/// What this machine has to offer, as the driver reports it.
/// </summary>
/// <remarks>
/// Separate from <see cref="AcceleratorChoice"/> because it answers a different question. That
/// one decides which torch build to download; this one is the ceiling on what can be promised to
/// the mesh, and a cap above it is a promise the hardware cannot keep.
/// </remarks>
/// <param name="GpuName">What the driver calls the card.</param>
/// <param name="TotalGb">Memory on the card, in GB.</param>
public sealed record GraphicsMemory(string GpuName, double TotalGb)
{
    /// <summary>The smallest backoff worth keeping, whatever the card.</summary>
    private const double MinimumBackoffGb = 1.5d;

    /// <summary>The share of the card held back before anything else asks for it.</summary>
    private const double BackoffShare = 0.25d;

    /// <summary>
    /// Memory held back from the mesh: a quarter of the card, and never less than 1.5 GB.
    /// </summary>
    /// <remarks>
    /// The thing competing for this memory is usually the person's own local inference, which is
    /// the reason the application exists, so the default must not starve it. A quarter is enough
    /// for a small model to keep running alongside whatever the mesh puts here, and the floor
    /// stops a small card from offering everything and leaving nothing.
    /// </remarks>
    public double BackoffGb => Math.Ceiling(Math.Max(TotalGb * BackoffShare, MinimumBackoffGb) * 2d) / 2d;

    /// <summary>
    /// The most that can be shared without eating into the backoff, rounded down to a half
    /// gigabyte. This is the ceiling of the slider until somebody asks for the whole card.
    /// </summary>
    public double SafeCeilingGb => Math.Max(0d, Math.Floor((TotalGb - BackoffGb) * 2d) / 2d);

    /// <summary>How the backoff was arrived at, for the panel to show.</summary>
    public string BackoffSummary =>
        $"{BackoffGb:0.#} GB is held back for your own models, a quarter of the card and never less than {MinimumBackoffGb:0.#} GB.";
}
