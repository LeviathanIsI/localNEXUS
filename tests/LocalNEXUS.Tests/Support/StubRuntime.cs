using LocalNEXUS.App.Services.Inference;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// A runtime that serves one format and starts nothing.
/// </summary>
/// <remarks>
/// Exists to hold the resolver to its stated shape: adding a runtime is one entry and nothing
/// else. A resolver that knew about llama.cpp or Python specifically could not pick a runtime
/// defined out here, so a stub winning is the assertion.
/// </remarks>
public sealed class StubRuntime : IModelRuntime
{
    private readonly ModelFormat _serves;

    public StubRuntime(string name, ModelFormat serves)
    {
        Name = name;
        _serves = serves;
    }

    public string Name { get; }

    /// <summary>How many times anything asked this to bring a model up.</summary>
    public int ServeCount { get; private set; }

    public bool CanServe(ModelDescriptor model) => model.Format == _serves;

    public Task<RuntimeEndpoint> EnsureServingAsync(
        ModelDescriptor model,
        ModelRuntimeOptions options,
        IProgress<string>? status,
        CancellationToken ct)
    {
        ServeCount++;
        return Task.FromResult(new RuntimeEndpoint("http://127.0.0.1:0", model.DisplayName));
    }

    public void ShutdownAll()
    {
    }
}
