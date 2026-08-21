using System.Diagnostics;
using System.IO;
using LocalNEXUS.Installer.Models;

namespace LocalNEXUS.Installer.Services;

/// <summary>What was found, and what follows from it.</summary>
/// <param name="Flavour">The build this machine should get.</param>
/// <param name="Summary">The sentence shown above the build options.</param>
/// <param name="GpuName">The card, when one answered.</param>
/// <param name="DriverVersion">The driver, when one answered.</param>
public sealed record GpuReport(LlamaFlavour Flavour, string Summary, string? GpuName, string? DriverVersion);

/// <summary>
/// Works out which llama.cpp build this machine should get.
/// </summary>
/// <remarks>
/// The rule, and why it is about the driver rather than the card.
///
/// A CUDA build is compiled against a CUDA major version, and a CUDA major version has a minimum
/// display driver. The card is almost irrelevant: an RTX 4080 on an old driver cannot run the
/// CUDA 13 build, and the failure is not a refusal. The v0.5 investigation found a CUDA bundle on
/// a CUDA 13 era driver reporting zero GPUs and silently falling back to the processor, which
/// presents as the application being slow for no reason. So vendor detection alone is not enough
/// and never was.
///
/// The rule:
///   an NVIDIA driver of 580 or newer      gives CUDA 13
///   an NVIDIA driver older than 580        gives CUDA 12
///   any other display adapter              gives Vulkan
///   no display adapter at all              gives processor only
///
/// 580 is the floor for the CUDA 13 runtime, and is the same floor AcceleratorProbe already uses
/// in the application to choose a torch wheel, for exactly the same reason.
///
/// nvidia-smi is asked first because it reports the display driver version directly. WMI is the
/// fallback and only has to answer whether any adapter exists, since a non NVIDIA adapter gets
/// Vulkan regardless of which one it is. On a laptop with both an integrated and a discrete
/// adapter, nvidia-smi answers for the discrete card, which is the one worth building for.
/// </remarks>
public sealed class GpuDetector
{
    /// <summary>The oldest NVIDIA driver the CUDA 13 build runs on.</summary>
    public const int MinimumCuda13Driver = 580;

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(12);

    /// <summary>Asks the machine what it has. Never throws.</summary>
    public GpuReport Detect()
    {
        if (TryNvidia() is { } nvidia)
        {
            return nvidia;
        }

        if (TryAnyAdapter() is { } adapter)
        {
            return new GpuReport(
                LlamaFlavour.Vulkan,
                $"Detected {adapter}, which is not an NVIDIA card, so the Vulkan build is selected. It runs on AMD and Intel graphics.",
                adapter,
                null);
        }

        return new GpuReport(
            LlamaFlavour.Cpu,
            "No graphics card answered, so the processor only build is selected. It works, and it is slow.",
            null,
            null);
    }

    private static GpuReport? TryNvidia()
    {
        var line = Run("nvidia-smi", "--query-gpu=driver_version,name --format=csv,noheader");

        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var comma = line.IndexOf(',');

        if (comma <= 0)
        {
            return null;
        }

        var driver = line[..comma].Trim();
        var name = line[(comma + 1)..].Trim();
        var major = Major(driver);

        if (major >= MinimumCuda13Driver)
        {
            return new GpuReport(
                LlamaFlavour.Cuda13,
                $"Detected a {name}, driver {driver}, new enough for the CUDA 13 build.",
                name,
                driver);
        }

        if (major > 0)
        {
            return new GpuReport(
                LlamaFlavour.Cuda12,
                $"Detected a {name}, driver {driver}, older than the {MinimumCuda13Driver} the CUDA 13 build needs, so CUDA 12 is selected.",
                name,
                driver);
        }

        // A driver string that will not parse is a reason to take the safe option rather than to
        // gamble half a gigabyte on a guess.
        return new GpuReport(
            LlamaFlavour.Vulkan,
            $"Detected a {name}, but its driver version could not be read, so the Vulkan build is selected because it runs on anything.",
            name,
            driver);
    }

    private static string? TryAnyAdapter()
    {
        var output = Run(
            "powershell",
            "-NoProfile -Command \"(Get-CimInstance Win32_VideoController | Select-Object -First 1 -ExpandProperty Name)\"");

        return string.IsNullOrWhiteSpace(output) ? null : output.Trim();
    }

    private static int Major(string version)
    {
        var head = version.Trim();
        var dot = head.IndexOf('.');

        if (dot > 0)
        {
            head = head[..dot];
        }

        return int.TryParse(head, out var parsed) ? parsed : 0;
    }

    private static string? Run(string fileName, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });

            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit((int)ProbeTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }

                return null;
            }

            var text = process.StandardOutput.ReadToEnd();

            return string.IsNullOrWhiteSpace(text)
                ? null
                : text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No such command, which for nvidia-smi is the ordinary answer on a machine with no
            // NVIDIA driver and is not an error.
            return null;
        }
    }
}
