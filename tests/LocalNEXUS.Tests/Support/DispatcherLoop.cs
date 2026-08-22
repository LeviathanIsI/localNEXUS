using System.Windows.Threading;

namespace LocalNEXUS.Tests.Support;

/// <summary>
/// A dispatcher on a thread that is actually pumping messages.
/// </summary>
/// <remarks>
/// Necessary, and the reason is worth writing down because it cost an afternoon to find. Several
/// services marshal onto the user interface thread before touching an observable collection, which
/// is correct and is what stops a node running on the thread pool corrupting a bound list. They do
/// it by asking the dispatcher, and a dispatcher only completes that call if something is running
/// its message loop.
///
/// <c>Dispatcher.CurrentDispatcher</c> on a test thread creates one and nothing ever runs it, so
/// the first marshalled call from a background thread blocks forever and the whole suite hangs
/// with no failure and no output. So this owns a thread whose only job is to pump, which is what
/// the application's user interface thread is doing in real use.
///
/// Nothing in the application was changed for this. The services are behaving correctly; the test
/// host simply has to provide the thread they are written against.
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
            Name = "localnexus-test-dispatcher"
        };

        // Single threaded apartment, as a user interface thread is, so anything that cares about
        // the apartment behaves the way it does in the application.
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
