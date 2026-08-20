namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Something that can serve a local model over the OpenAI compatible API.
/// </summary>
/// <remarks>
/// Every runtime hides behind the same three questions: can you serve this, bring it up and
/// tell me where, and stop. Because each one exposes the same protocol once it is up, the model
/// client, the endpoint and the streaming path are the same for all of them, and adding a
/// runtime never touches the request path. A runtime owns its own child processes and its own
/// environment; nothing is loaded into this process.
/// </remarks>
public interface IModelRuntime
{
    /// <summary>The runtime's name, as used in progress messages and refusals.</summary>
    string Name { get; }

    /// <summary>Whether this runtime is the one that serves models of that shape.</summary>
    bool CanServe(ModelDescriptor model);

    /// <summary>
    /// Brings the model up if it is not already, and returns where to send requests.
    /// </summary>
    /// <param name="model">The model to serve, already detected.</param>
    /// <param name="options">Per launch settings from the node asking for it.</param>
    /// <param name="status">Receives progress while the model loads.</param>
    /// <param name="ct">Cancels the wait. A server that is already loading keeps loading.</param>
    /// <exception cref="ModelClientException">The runtime is unavailable, or the server failed to become healthy.</exception>
    Task<RuntimeEndpoint> EnsureServingAsync(
        ModelDescriptor model,
        ModelRuntimeOptions options,
        IProgress<string>? status,
        CancellationToken ct);

    /// <summary>Stops everything this runtime has running. Called when the application exits.</summary>
    void ShutdownAll();
}
