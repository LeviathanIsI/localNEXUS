namespace LocalNEXUS.App.Infrastructure;

/// <summary>
/// A progress sink that invokes its handler synchronously on the reporting thread.
/// </summary>
/// <remarks>
/// The framework's <see cref="Progress{T}"/> posts to a captured synchronisation context, which
/// on a background thread means the thread pool and offers no ordering guarantee. Streamed
/// tokens must arrive in the order they were produced, so this reports inline instead.
/// </remarks>
public sealed class DelegateProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    public DelegateProgress(Action<T> handler) => _handler = handler;

    public void Report(T value) => _handler(value);
}
