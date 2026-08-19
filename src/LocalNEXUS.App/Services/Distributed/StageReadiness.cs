namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// What the mesh currently knows about one section of a model.
/// </summary>
/// <remarks>
/// The distinction that matters is between "we know this section cannot serve" and "we do not
/// know yet". Only the first two of those are a verdict; <see cref="Pending"/> and
/// <see cref="Loading"/> are the ordinary condition of a mesh that is still coming up, and must
/// never be rendered as a failure. The value is mapped from the engine's own stage report in
/// one place, so an engine word nobody has seen before settles as loading rather than as a
/// false alarm.
/// </remarks>
public enum StageReadiness
{
    /// <summary>The mesh has not placed this section, or has not reported on it yet.</summary>
    Pending,

    /// <summary>A source holds this section and is bringing it up.</summary>
    Loading,

    /// <summary>Loaded and serving.</summary>
    Ready,

    /// <summary>The mesh planned this section onto a node it no longer lists. A known gap.</summary>
    Missing,

    /// <summary>The source holding this section reports it stopped or failed. A known failure.</summary>
    Failed
}
