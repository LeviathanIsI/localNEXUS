using System.IO;

namespace LocalNEXUS.App.Services.Inference;

/// <summary>
/// Picks the runtime that serves a given local model, and serves it.
/// </summary>
/// <remarks>
/// This is the whole of the application's knowledge that there is more than one runtime. A model
/// node asks for a path and gets back somewhere to send requests; it never learns what format
/// the file was or which process ended up serving it, which is what keeps "local" meaning
/// "whatever local runtime can serve this" rather than "llama.cpp".
/// </remarks>
public sealed class RuntimeResolver
{
    private readonly IReadOnlyList<IModelRuntime> _runtimes;

    public RuntimeResolver(params IModelRuntime[] runtimes) => _runtimes = runtimes;

    /// <summary>Every runtime this build knows about, in the order they are asked.</summary>
    public IReadOnlyList<IModelRuntime> Runtimes => _runtimes;

    /// <summary>
    /// Detects what the path holds and returns the runtime that can serve it.
    /// </summary>
    /// <exception cref="ModelClientException">Nothing here can serve it, with the reason why.</exception>
    public IModelRuntime Resolve(ModelDescriptor model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (!model.IsServable)
        {
            throw new ModelClientException(
                model.UnsupportedReason is { Length: > 0 } reason
                    ? $"{model.Path} cannot be served. {reason}"
                    : $"{model.Path} is not a model this build recognises.");
        }

        foreach (var runtime in _runtimes)
        {
            if (runtime.CanServe(model))
            {
                return runtime;
            }
        }

        throw new ModelClientException(
            $"{model.DisplayName} is {model.FormatLabel}, and no runtime in this build serves that format.");
    }

    /// <summary>
    /// Brings a model up on whichever runtime serves it, and says where to send requests.
    /// </summary>
    /// <exception cref="ModelClientException">The path is not a servable model, or the runtime could not start it.</exception>
    public async Task<RuntimeEndpoint> ServeAsync(
        string path,
        ModelRuntimeOptions options,
        IProgress<string>? status,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ModelClientException("No local model is selected for this node.");
        }

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            throw new ModelClientException($"The model no longer exists: {path}");
        }

        var model = ModelFormatDetector.Describe(path);
        var runtime = Resolve(model);

        status?.Report($"{model.DisplayName} is {model.FormatLabel}, served by {runtime.Name}");

        return await runtime.EnsureServingAsync(model, options, status, ct).ConfigureAwait(false);
    }

    /// <summary>Stops every runtime. Called when the application exits.</summary>
    public void ShutdownAll()
    {
        foreach (var runtime in _runtimes)
        {
            runtime.ShutdownAll();
        }
    }
}
