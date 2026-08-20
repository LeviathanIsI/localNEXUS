using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LocalNEXUS.App.Services.Processes;

/// <summary>
/// A Windows job object that owns every child process this application starts.
/// </summary>
/// <remarks>
/// This is the only mechanism on Windows that survives the application dying without running any
/// code of its own. A process assigned to a job cannot leave it, every process it goes on to
/// start joins the same job automatically, and the kernel terminates the whole job when the last
/// handle to it closes. That closing happens whether the application exits normally, is killed
/// outright, or faults, which is what makes shutdown deterministic rather than best effort.
///
/// It also answers the identification question exactly: membership of this job is the definition
/// of a process this application started, so a node the user is running themselves can never be
/// mistaken for one of ours.
/// </remarks>
internal sealed class JobObject : IDisposable
{
    private const int ExtendedLimitInformation = 9;
    private const int BasicProcessIdList = 3;
    private const uint KillOnJobClose = 0x2000;
    private const int ErrorMoreData = 234;

    private readonly IntPtr _handle;

    private bool _disposed;

    private JobObject(IntPtr handle) => _handle = handle;

    /// <summary>
    /// Creates the job, or returns null when the operating system refuses. A null result is not
    /// fatal: the caller still terminates children explicitly, it just loses the backstop that
    /// covers the application being killed.
    /// </summary>
    public static JobObject? TryCreate()
    {
        var handle = CreateJobObjectW(IntPtr.Zero, null);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var limits = new JobObjectExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = KillOnJobClose;

        var size = Marshal.SizeOf<JobObjectExtendedLimitInformation>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(limits, buffer, fDeleteOld: false);

            if (!SetInformationJobObject(handle, ExtendedLimitInformation, buffer, (uint)size))
            {
                CloseHandle(handle);
                return null;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return new JobObject(handle);
    }

    /// <summary>
    /// Puts a process under the job. Returns false when the assignment was refused, which the
    /// caller reports rather than treats as fatal.
    /// </summary>
    public bool TryAssign(Process process)
    {
        if (_disposed)
        {
            return false;
        }

        try
        {
            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The process exited between starting and being assigned, which needs no job.
            return false;
        }
    }

    /// <summary>
    /// Every process currently in the job. This is how termination is verified: an empty list is
    /// proof, where an engine's own report of what it stopped is not.
    /// </summary>
    public IReadOnlyList<int> GetProcessIds()
    {
        if (_disposed)
        {
            return Array.Empty<int>();
        }

        // Header is two DWORDs followed by one pointer sized id per process.
        var capacity = 64;

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var size = 8 + (capacity * IntPtr.Size);
            var buffer = Marshal.AllocHGlobal(size);

            try
            {
                if (!QueryInformationJobObject(_handle, BasicProcessIdList, buffer, (uint)size, out _))
                {
                    if (Marshal.GetLastWin32Error() != ErrorMoreData)
                    {
                        return Array.Empty<int>();
                    }

                    capacity *= 4;
                    continue;
                }

                var count = Marshal.ReadInt32(buffer, 4);
                var ids = new List<int>(count);

                for (var i = 0; i < count; i++)
                {
                    ids.Add((int)Marshal.ReadIntPtr(buffer, 8 + (i * IntPtr.Size)));
                }

                return ids;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        return Array.Empty<int>();
    }

    /// <summary>
    /// Kills every process in the job in one call. Unlike walking a process tree, this cannot
    /// race a child that spawns another while the walk is in progress, because the new process
    /// is already in the job before it runs.
    /// </summary>
    public void Terminate()
    {
        if (!_disposed)
        {
            TerminateJobObject(_handle, 0);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Closing the last handle is what makes the kernel kill anything still inside.
        CloseHandle(_handle);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr securityAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length, out uint returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    // The interop layouts below are written by the operating system rather than by this code,
    // so most of their fields are never assigned here on purpose.
#pragma warning disable CS0649
    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
#pragma warning restore CS0649
}
