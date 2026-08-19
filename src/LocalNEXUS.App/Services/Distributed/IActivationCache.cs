namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// The failover seam for mid pipeline recovery: caching the activations that cross section
/// boundaries so that when a source drops, the sections before the break do not have to be
/// recomputed and only the lost section is re-attempted against another source. This is the
/// technique Petals uses.
/// </summary>
/// <remarks>
/// Deliberately unwired. llama.cpp's RPC backend executes the whole compute graph internally
/// and exposes no hook at the section boundary: this was verified against the bundled build
/// (b10488), whose rpc-server offers only host, port, threads, device and cache flags, and
/// whose coordinator publishes no per backend state over HTTP. Until the engine exposes the
/// boundary, failover works at whole request granularity: the run is re-planned against the
/// sources still covering each section and the request is re-sent from the start. Nothing may
/// be designed around the absence of this seam; when the hook appears, implementations plug
/// in here and the re-planning path starts asking the cache before recomputing.
/// </remarks>
public interface IActivationCache
{
    /// <summary>
    /// Returns the cached activation leaving the given section for the given request, when
    /// one is held.
    /// </summary>
    bool TryGetActivation(Guid requestId, int sectionIndex, out ReadOnlyMemory<byte> activation);

    /// <summary>Stores the activation leaving a section, replacing any previous value.</summary>
    void StoreActivation(Guid requestId, int sectionIndex, ReadOnlyMemory<byte> activation);

    /// <summary>Releases everything held for a request once it completes or is abandoned.</summary>
    void DropRequest(Guid requestId);
}
