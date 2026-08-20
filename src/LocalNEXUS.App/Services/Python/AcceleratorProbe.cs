using System.Diagnostics;
using System.Globalization;

namespace LocalNEXUS.App.Services.Python;

/// <summary>
/// Works out which torch build this machine can run, by asking the driver rather than guessing.
/// </summary>
/// <remarks>
/// The CUDA wheels pinned by this build are CUDA 13, which needs an r580 or newer NVIDIA driver.
/// A machine below that floor gets the processor build: slower, but it starts, which is a better
/// answer than a 1.8 GB download that fails to import. The driver version is what is checked
/// because it is what actually constrains the wheel, and a driver is upgradeable without a new
/// graphics card.
/// </remarks>
public static class AcceleratorProbe
{
    /// <summary>Lockfile pinning the CUDA 13.2 build of torch.</summary>
    public const string CudaLockfileName = "requirements-cu132.txt";

    /// <summary>Lockfile pinning the processor only build of torch.</summary>
    public const string CpuLockfileName = "requirements-cpu.txt";

    /// <summary>The oldest NVIDIA driver the CUDA 13 wheels run on.</summary>
    private const int MinimumCudaDriverMajor = 580;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Asks nvidia-smi what is present. Never throws: a machine with no NVIDIA driver at all is
    /// the ordinary case for the processor build, not an error.
    /// </summary>
    public static AcceleratorChoice Detect()
    {
        var report = QueryDriver();

        if (report is null)
        {
            return new AcceleratorChoice(
                PythonAccelerator.Cpu,
                CpuLockfileName,
                "No NVIDIA driver answered, so the processor build of torch was chosen.");
        }

        var (driverVersion, gpuName, _) = report.Value;

        if (!TryReadMajor(driverVersion, out var major))
        {
            return new AcceleratorChoice(
                PythonAccelerator.Cpu,
                CpuLockfileName,
                $"The NVIDIA driver reported version '{driverVersion}', which could not be read, so the processor build of torch was chosen.");
        }

        if (major < MinimumCudaDriverMajor)
        {
            return new AcceleratorChoice(
                PythonAccelerator.Cpu,
                CpuLockfileName,
                $"{gpuName} has driver {driverVersion}, older than the {MinimumCudaDriverMajor} this build's CUDA wheels need, so the processor build of torch was chosen. Updating the driver and repairing the environment switches it to CUDA.");
        }

        return new AcceleratorChoice(
            PythonAccelerator.Cuda,
            CudaLockfileName,
            $"{gpuName} with driver {driverVersion}, so the CUDA build of torch was chosen.");
    }

    /// <summary>
    /// How much memory this machine's GPU has, or null when no driver answered.
    /// </summary>
    /// <remarks>
    /// The same query as <see cref="Detect"/>, because there is no reason to ask the driver twice
    /// and every reason not to have two places that decide what hardware is present. Cached: the
    /// answer does not change while the application is running, and the contribution panel reads
    /// it every time it is drawn.
    /// </remarks>
    public static GraphicsMemory? DetectMemory()
    {
        if (_memory is not null)
        {
            return _memory;
        }

        var report = QueryDriver();

        if (report is not { } found || found.TotalMemoryMb <= 0)
        {
            return null;
        }

        _memory = new GraphicsMemory(found.GpuName, found.TotalMemoryMb / 1024d);
        return _memory;
    }

    private static GraphicsMemory? _memory;

    private static (string DriverVersion, string GpuName, double TotalMemoryMb)? QueryDriver()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "nvidia-smi",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("--query-gpu=driver_version,name,memory.total");
        startInfo.ArgumentList.Add("--format=csv,noheader,nounits");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or AggregateException or System.ComponentModel.Win32Exception)
                {
                    // The probe is advisory. Whatever state it is in, the answer is no driver.
                }

                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            var firstLine = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (firstLine is null)
            {
                return null;
            }

            var parts = firstLine.Split(',', StringSplitOptions.TrimEntries);

            var name = parts.Length >= 2 ? parts[1] : "This machine's NVIDIA GPU";

            // Memory is asked for without units, so a card that does not report it reads as zero
            // rather than as a number nobody can interpret.
            var memoryMb = parts.Length >= 3
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0d;

            return (parts[0], name, memoryMb);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // nvidia-smi is not on the path, which is what a machine without the driver looks like.
            return null;
        }
    }

    private static bool TryReadMajor(string driverVersion, out int major)
    {
        var head = driverVersion.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out major);
    }
}
