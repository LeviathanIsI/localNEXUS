using System.Diagnostics;
using System.IO;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Processes;

/// <summary>
/// Owns the lifetime of every engine process this application starts, and guarantees that none
/// of them outlives it.
/// </summary>
/// <remarks>
/// Shutdown used to be a single tree kill per manager, fired once and assumed to have worked.
/// The reason that failed is worth stating plainly, because it defeats every approach built on
/// process handles: the mesh executable is a launcher that re-executes itself and then exits, so
/// the process this application started is legitimately gone while the node it left behind is
/// still running and is no longer anybody's child. A tree kill aimed at the launcher finds an
/// exited process with no children and reports success. Whether that happened before or after
/// the window closed is what made the orphan intermittent.
///
/// So a process handle is not treated as the authority on anything here. Each child is given its
/// own Windows job object, every process it goes on to start joins that job automatically and
/// cannot leave it, and the job's own process list is what says whether anything is still alive.
/// Termination is asked for, then forced, then verified against that list, then retried. Closing
/// the job is the last word: the kernel kills whatever is still inside when the application's
/// handle goes, which happens however the application ends, including being killed outright with
/// no chance to run code of its own.
///
/// Everything is scoped to processes this application started. Job membership decides that for a
/// live session and the registry decides it for a session that never got to finish, so an engine
/// the user is running themselves is never a candidate.
/// </remarks>
public sealed class ChildProcessGroup : IDisposable
{
    /// <summary>
    /// How long a child is given to stop after being asked politely. Deliberately short, because
    /// neither bundled engine acts on the request at all and every close pays this wait.
    /// </summary>
    private static readonly TimeSpan GracefulWait = TimeSpan.FromSeconds(1);

    /// <summary>How long one forced pass is given before the survivors are counted.</summary>
    private static readonly TimeSpan KillWait = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How many times termination is forced and re-checked before giving up on it.</summary>
    private const int MaxPasses = 3;

    private readonly object _sync = new();
    private readonly List<TrackedChild> _children = new();
    private readonly ChildProcessRegistry _registry;

    private StreamWriter? _log;
    private bool _jobsAvailable = true;
    private bool _disposed;

    public ChildProcessGroup()
        : this(new ChildProcessRegistry())
    {
    }

    public ChildProcessGroup(ChildProcessRegistry registry) => _registry = registry;

    /// <summary>
    /// True while the kernel level backstop is in place, meaning children die with this
    /// application even if it is killed outright.
    /// </summary>
    public bool HasKernelBackstop => _jobsAvailable;

    /// <summary>
    /// Brings a freshly started process, and everything it goes on to start, under this group's
    /// ownership.
    /// </summary>
    /// <remarks>
    /// Called immediately after starting the process. There is a very small window before the
    /// assignment lands in which the child could start a child of its own that escapes the job;
    /// the registry and the verified retry below are what close it.
    /// </remarks>
    public void Track(Process process, string role)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var job = JobObject.TryCreate();

        if (job is null)
        {
            _jobsAvailable = false;
            Log($"Windows refused a job object for the {role} process. It will still be terminated explicitly, but a hard kill of this application could leave it behind.");
        }
        else if (!job.TryAssign(process))
        {
            _jobsAvailable = false;
            Log($"The {role} process {SafeId(process)} could not be assigned to its job object.");
            job.Dispose();
            job = null;
        }

        lock (_sync)
        {
            _children.Add(new TrackedChild(process, job, role));
        }

        _registry.Record(process, role);
    }

    /// <summary>
    /// Stops one child and everything it started, and confirms they are gone. Used when the user
    /// stops the mesh node or a local server is replaced, so the rest of the group keeps running.
    /// </summary>
    public void Terminate(Process process)
    {
        TrackedChild? child;
        lock (_sync)
        {
            child = _children.FirstOrDefault(c => ReferenceEquals(c.Process, process));
        }

        if (child is null)
        {
            // Not one of ours to stop. Saying so is better than killing something on a guess.
            Log($"Asked to terminate process {SafeId(process)}, which this group never started. Leaving it alone.");
            return;
        }

        Stop(child);

        lock (_sync)
        {
            _children.Remove(child);
        }

        child.Dispose();
    }

    /// <summary>
    /// Stops every child. Safe to call more than once, because every shutdown path calls it.
    /// </summary>
    public void ShutdownAll()
    {
        TrackedChild[] children;
        lock (_sync)
        {
            children = _children.ToArray();
            _children.Clear();
        }

        foreach (var child in children)
        {
            Stop(child);
            child.Dispose();
        }

        _registry.ForgetOwn();
    }

    /// <summary>
    /// Deals with engine processes a previous session left behind, and reports how many.
    /// </summary>
    /// <remarks>
    /// They are terminated rather than adopted and reused. A leftover node was launched with the
    /// previous session's settings, which is a configuration this session has no way to read back
    /// out of it and no reason to trust, and it is holding the ports and the memory a fresh one
    /// needs. Restarting costs nothing that matters, because the engine keeps its identity in its
    /// own key file, so the node rejoins the mesh as the same peer it was.
    /// </remarks>
    public int TerminateAbandoned()
    {
        var abandoned = _registry.FindAbandoned();
        var stopped = 0;

        foreach (var record in abandoned)
        {
            Log($"A previous session left {record.Role} process {record.Pid} running ({record.ExecutablePath}). Stopping it.");

            try
            {
                using var process = Process.GetProcessById(record.Pid);
                KillTree(process, pass: 1);

                if (process.WaitForExit((int)KillWait.TotalMilliseconds))
                {
                    stopped++;
                }
                else
                {
                    Log($"Process {record.Pid} did not stop.");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                Log($"Process {record.Pid} was already gone.");
            }

            _registry.Forget(record.Pid);
        }

        return stopped;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ShutdownAll();

        _log?.Dispose();
        _log = null;
    }

    /// <summary>
    /// Asks a child to stop, forces it, then checks whether anything it started is still alive
    /// and tries again. The job's process list is what is checked: the process this application
    /// started may have exited long ago and handed the work to a process it left behind.
    /// </summary>
    private void Stop(TrackedChild child)
    {
        // Ask first. Neither bundled engine honours this today, which was tested rather than
        // assumed: mesh-llm ignores its standard input closing, exposes no shutdown route on its
        // management API, and its own stop command takes no target so it cannot be aimed at one
        // process. The step stays because it costs one bounded wait and is where a future engine
        // that does stop on request gets wired in.
        RequestStop(child.Process);
        WaitFor(() => IsFinished(child), GracefulWait);

        for (var pass = 1; pass <= MaxPasses && !IsFinished(child); pass++)
        {
            var alive = child.LivePids();
            Log($"{child.Role}: pass {pass}, still running: {Describe(alive)}.");

            // One call kills every process in the job at once, so a child that re-executed
            // itself while the pass was being prepared is already inside what is being killed.
            child.Job?.Terminate();
            KillTree(child.Process, pass);

            WaitFor(() => IsFinished(child), KillWait);

            var remaining = child.LivePids();
            Log(remaining.Count == 0
                ? $"{child.Role}: pass {pass} stopped everything."
                : $"{child.Role}: pass {pass} left {Describe(remaining)} alive.");
        }

        if (!IsFinished(child))
        {
            Log($"{child.Role}: gave up after {MaxPasses} passes with {Describe(child.LivePids())} alive. Closing the job object is the last word.");
        }

        _registry.Forget(SafeId(child.Process));
    }

    /// <summary>
    /// True when nothing this child started is running. Without a job object there is nothing
    /// better to go on than the handle, which is exactly the case the job object exists to fix.
    /// </summary>
    private static bool IsFinished(TrackedChild child)
        => child.Job is null ? HasExited(child.Process) : child.LivePids().Count == 0;

    /// <summary>
    /// True when the operating system still has this process. Asked of every process the job has
    /// ever held, because the kernel drops a process from its job as soon as termination begins
    /// while the process itself takes a moment longer to go, and reporting it stopped in that
    /// window would let a restart collide with a node still holding the port.
    /// </summary>
    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Asks a process to stop. Closing its standard input is the only request that can be
    /// delivered to a child of a windowed application: it has no console of its own, so console
    /// control events have nowhere to go.
    /// </summary>
    private static void RequestStop(Process process)
    {
        try
        {
            if (!process.HasExited && process.StartInfo.RedirectStandardInput)
            {
                process.StandardInput.Close();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            // Nothing to ask, which only means the forced pass does the work.
        }
    }

    private void KillTree(Process process, int pass)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (AggregateException ex)
        {
            // A tree kill reports every member it could not kill this way. Left uncaught, as it
            // was, it abandoned the rest of shutdown.
            Log($"Pass {pass}: tree kill of {SafeId(process)} reported {ex.InnerExceptions.Count} failure(s): {ex.InnerExceptions[0].Message}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            // Already gone, which is the outcome that was wanted.
        }
    }

    private static string Describe(IReadOnlyList<int> pids)
        => pids.Count == 0 ? "nothing" : string.Join(", ", pids);

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    private static int SafeId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static void WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>
    /// Writes a line about what shutdown found. The activity feed is gone by the time most of
    /// this runs, so the file is the only place it can be read afterwards.
    /// </summary>
    private void Log(string message)
    {
        try
        {
            _log ??= new StreamWriter(AppPaths.CreateLogFilePath("processes"), append: true) { AutoFlush = true };
            _log.WriteLine($"{DateTimeOffset.Now:O}  {message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // Losing the log must never be what stops a process from being killed.
        }
    }

    /// <summary>One engine this application started, and the job holding everything it spawned.</summary>
    private sealed class TrackedChild : IDisposable
    {
        /// <summary>
        /// Every process id the job has ever held. A process is dropped from the job the moment
        /// it is told to die, so this is what makes the difference between told to stop and
        /// actually stopped. Reuse of an id inside the second or two a shutdown takes is not a
        /// practical concern.
        /// </summary>
        private readonly HashSet<int> _seen = new();

        public TrackedChild(Process process, JobObject? job, string role)
        {
            Process = process;
            Job = job;
            Role = role;
        }

        public Process Process { get; }

        public JobObject? Job { get; }

        public string Role { get; }

        /// <summary>Every process still running under this child, which is the only honest measure.</summary>
        public IReadOnlyList<int> LivePids()
        {
            if (Job is null)
            {
                return Array.Empty<int>();
            }

            var inJob = Job.GetProcessIds();
            foreach (var pid in inJob)
            {
                _seen.Add(pid);
            }

            var live = new HashSet<int>(inJob);

            foreach (var pid in _seen)
            {
                if (IsAlive(pid))
                {
                    live.Add(pid);
                }
            }

            return live.OrderBy(pid => pid).ToList();
        }

        /// <summary>Closing the job is what makes the kernel kill anything left inside it.</summary>
        public void Dispose() => Job?.Dispose();
    }
}
