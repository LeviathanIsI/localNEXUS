using System.Diagnostics;
using System.IO;
using System.Text.Json;
using LocalNEXUS.App.Services.Persistence;

namespace LocalNEXUS.App.Services.Processes;

/// <summary>
/// The on disk list of engine processes this application has started.
/// </summary>
/// <remarks>
/// The job object handles every case where the application is still around to be killed. This
/// file covers the one case it cannot: the machine losing power, or a job assignment being
/// refused, leaving a process behind with nobody to answer for it. Reading it at startup is how
/// the next session recognises those.
///
/// Nothing here decides to kill anything. It reports which recorded processes are still alive
/// and provably ours, and the caller decides what to do about them.
/// </remarks>
public sealed class ChildProcessRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    /// <summary>Start times are compared with a tolerance because they round trip through JSON.</summary>
    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(2);

    private readonly object _sync = new();
    private readonly string _path;
    private readonly int _ownerPid;
    private readonly DateTimeOffset _ownerStartedUtc;

    private List<ChildProcessRecord> _records = new();

    public ChildProcessRegistry()
        : this(AppPaths.ChildProcessFile)
    {
    }

    public ChildProcessRegistry(string path)
    {
        _path = path;

        using var self = Process.GetCurrentProcess();
        _ownerPid = self.Id;
        _ownerStartedUtc = self.StartTime.ToUniversalTime();

        _records = Read();
    }

    /// <summary>Adds a process this session started and writes the file out.</summary>
    public void Record(Process process, string role)
    {
        ChildProcessRecord record;

        try
        {
            record = new ChildProcessRecord
            {
                Pid = process.Id,
                StartedUtc = process.StartTime.ToUniversalTime(),
                ExecutablePath = process.MainModule?.FileName ?? string.Empty,
                Role = role,
                OwnerPid = _ownerPid,
                OwnerStartedUtc = _ownerStartedUtc
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // The process exited before it could be described. Nothing to remember.
            return;
        }

        lock (_sync)
        {
            _records.RemoveAll(r => r.Pid == record.Pid);
            _records.Add(record);
            Write();
        }
    }

    /// <summary>Drops a process that has been dealt with.</summary>
    public void Forget(int pid)
    {
        lock (_sync)
        {
            if (_records.RemoveAll(r => r.Pid == pid) > 0)
            {
                Write();
            }
        }
    }

    /// <summary>Drops everything this session recorded, which is what a clean shutdown leaves behind.</summary>
    public void ForgetOwn()
    {
        lock (_sync)
        {
            if (_records.RemoveAll(r => r.OwnerPid == _ownerPid) > 0)
            {
                Write();
            }
        }
    }

    /// <summary>
    /// Recorded processes that are still running, whose own owner is gone, and whose identity
    /// still matches on all three of id, start time and binary. Anything that fails any of those
    /// is left strictly alone.
    /// </summary>
    /// <remarks>
    /// Records left by a session that died, whose process has since gone on its own, are dropped
    /// here. Nothing else would ever remove them, and a file that only grows is a file that
    /// eventually costs every startup something.
    /// </remarks>
    public IReadOnlyList<ChildProcessRecord> FindAbandoned()
    {
        List<ChildProcessRecord> candidates;
        lock (_sync)
        {
            candidates = _records.ToList();
        }

        var abandoned = new List<ChildProcessRecord>();
        var dead = new List<ChildProcessRecord>();

        foreach (var record in candidates)
        {
            if (record.OwnerPid == _ownerPid || IsAlive(record.OwnerPid, record.OwnerStartedUtc, null))
            {
                // Another instance of the application is still answering for this one.
                continue;
            }

            if (IsAlive(record.Pid, record.StartedUtc, record.ExecutablePath))
            {
                abandoned.Add(record);
            }
            else
            {
                dead.Add(record);
            }
        }

        if (dead.Count > 0)
        {
            lock (_sync)
            {
                _records.RemoveAll(r => dead.Any(d => d.Pid == r.Pid && d.OwnerPid == r.OwnerPid));
                Write();
            }
        }

        return abandoned;
    }

    /// <summary>
    /// True when the process with this id is the same process the record describes. The start
    /// time settles process id reuse, and the executable path settles whether it is a binary
    /// this application launched rather than one the user is running themselves.
    /// </summary>
    private static bool IsAlive(int pid, DateTimeOffset startedUtc, string? executablePath)
    {
        try
        {
            using var process = Process.GetProcessById(pid);

            if (process.HasExited)
            {
                return false;
            }

            var started = process.StartTime.ToUniversalTime();
            if ((started - startedUtc).Duration() > StartTimeTolerance)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(executablePath))
            {
                var actual = process.MainModule?.FileName;
                if (!string.Equals(actual, executablePath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            // No such process, or one this account cannot inspect. Either way it is not ours.
            return false;
        }
    }

    private List<ChildProcessRecord> Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new List<ChildProcessRecord>();
            }

            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<List<ChildProcessRecord>>(json, SerializerOptions)
                   ?? new List<ChildProcessRecord>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new List<ChildProcessRecord>();
        }
    }

    private void Write()
    {
        try
        {
            AppPaths.EnsureCreated();
            File.WriteAllText(_path, JsonSerializer.Serialize(_records, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing the record costs a later session its backstop. It must never cost this one
            // its ability to start an engine.
        }
    }
}
