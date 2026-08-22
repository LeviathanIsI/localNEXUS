using System.Windows.Threading;

namespace LocalNEXUS.Evals;

/// <summary>
/// A dispatcher on a thread that is actually pumping messages.
/// </summary>
/// <remarks>
/// Several services marshal onto the user interface thread before touching an observable
/// collection, which is correct and is what stops a node running on the thread pool corrupting a
/// bound list. A dispatcher only completes that call if something is running its message loop, and
/// a console application has nothing running one, so the first marshalled call from a background
/// thread would block forever with no error and no output.
///
/// So this owns a thread whose only job is to pump, which is what the application's user interface
/// thread does in real use. Nothing in the application was changed for it; the services behave
/// correctly and the host has to supply the thread they are written against.
/// </remarks>
public sealed class DispatcherLoop : IDisposable
{
    private readonly Thread _thread;

    public DispatcherLoop()
    {
        using var ready = new ManualResetEventSlim(false);
        Dispatcher? dispatcher = null;

        _thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "localnexus-eval-dispatcher"
        };

        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        ready.Wait();
        Dispatcher = dispatcher!;
    }

    /// <summary>The dispatcher to hand to anything that marshals.</summary>
    public Dispatcher Dispatcher { get; }

    public void Dispose()
    {
        Dispatcher.InvokeShutdown();
        _thread.Join(TimeSpan.FromSeconds(5));
    }
}
