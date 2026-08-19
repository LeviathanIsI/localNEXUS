using System.Diagnostics;

namespace LocalNEXUS.App.Services.Distributed;

/// <summary>
/// Best effort detection of this machine's GPU memory, used to seed the local source's
/// declared capability. Asks nvidia-smi because that covers the hardware llama.cpp's CUDA
/// build runs on; on any other machine the answer is simply unknown and the user can type
/// a number into the panel instead.
/// </summary>
public static class GpuMemoryProbe
{
    /// <summary>Total GPU memory in MiB, or zero when it cannot be determined.</summary>
    public static long TryReadTotalMemoryMb()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--query-gpu=memory.total");
            startInfo.ArgumentList.Add("--format=csv,noheader,nounits");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return 0;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                process.Kill(entireProcessTree: true);
                return 0;
            }

            var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            return long.TryParse(firstLine, out var memoryMb) && memoryMb > 0 ? memoryMb : 0;
        }
        catch (Exception)
        {
            // No nvidia-smi, no NVIDIA driver, or an unreadable answer all mean the same
            // thing here: the capability is unknown until the user declares it.
            return 0;
        }
    }
}
