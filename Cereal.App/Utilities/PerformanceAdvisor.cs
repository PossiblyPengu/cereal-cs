using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Cereal.App.Utilities;

public sealed record HardwareSnapshot(
    string Cpu,
    string Gpu,
    string Os,
    ulong TotalMemoryBytes,
    int LogicalCores);

public sealed record PerformanceRecommendation(
    string Tier,
    string Summary,
    string StarDensity,
    string UiScale,
    bool ShowAnimations);

public static class PerformanceAdvisor
{
    public static HardwareSnapshot Detect()
    {
        var os = RuntimeInformation.OSDescription;
        var cpu = "—";
        var gpu = "—";
        ulong ramBytes = 0;
        var cores = Math.Max(1, Environment.ProcessorCount);

        if (OperatingSystem.IsWindows())
        {
            TryWindowsCpu(ref cpu);
            TryWindowsRam(ref ramBytes);
            TryWindowsGpu(ref gpu);
        }
        else if (OperatingSystem.IsLinux())
        {
            TryLinuxCpu(ref cpu);
            TryLinuxRam(ref ramBytes);
        }
        else if (OperatingSystem.IsMacOS())
        {
            // Minimal fallback for macOS without invoking shell processes.
            cpu = RuntimeInformation.ProcessArchitecture + " CPU";
        }

        return new HardwareSnapshot(cpu, gpu, os, ramBytes, cores);
    }

    public static PerformanceRecommendation Recommend(HardwareSnapshot snapshot)
    {
        var ramGb = snapshot.TotalMemoryBytes > 0
            ? snapshot.TotalMemoryBytes / 1_073_741_824.0
            : 0;

        var score = 0;
        if (snapshot.LogicalCores >= 16) score += 4;
        else if (snapshot.LogicalCores >= 12) score += 3;
        else if (snapshot.LogicalCores >= 8) score += 2;
        else if (snapshot.LogicalCores >= 6) score += 1;
        else score -= 1;

        if (ramGb >= 32) score += 4;
        else if (ramGb >= 16) score += 3;
        else if (ramGb >= 12) score += 2;
        else if (ramGb >= 8) score += 1;
        else if (ramGb > 0) score -= 2;

        score += ScoreGpu(snapshot.Gpu);

        var tier = score >= 7 ? "High" : score >= 3 ? "Balanced" : "Low";
        var summary = BuildSummary(tier, snapshot, ramGb);
        return tier switch
        {
            "High" => new PerformanceRecommendation(
                tier, summary, StarDensity: "high", UiScale: "100%", ShowAnimations: true),
            "Low" => new PerformanceRecommendation(
                tier, summary, StarDensity: "low", UiScale: "110%", ShowAnimations: false),
            _ => new PerformanceRecommendation(
                tier, summary, StarDensity: "normal", UiScale: "100%", ShowAnimations: true),
        };
    }

    public static string FormatRam(ulong bytes)
    {
        if (bytes == 0) return "—";
        return $"{Math.Round(bytes / 1_073_741_824.0, MidpointRounding.AwayFromZero)} GB";
    }

    private static int ScoreGpu(string gpu)
    {
        if (string.IsNullOrWhiteSpace(gpu) || gpu == "—") return 0;
        var g = gpu.ToLowerInvariant();

        if (ContainsAny(g, "rtx 50", "rtx 40", "rtx 30", "rtx 20", "rx 9", "rx 8", "rx 7", "arc a", "arc b"))
            return 3;
        if (ContainsAny(g, "gtx 16", "gtx 10", "rx 6", "rx 5", "vega", "iris xe", "radeon", "arc"))
            return 1;
        if (ContainsAny(g, "intel uhd", "intel hd", "microsoft basic"))
            return -2;
        return 0;
    }

    private static string BuildSummary(string tier, HardwareSnapshot s, double ramGb)
    {
        var ramText = ramGb > 0
            ? $"{ramGb.ToString("0.#", CultureInfo.InvariantCulture)} GB RAM"
            : "unknown RAM";
        return tier switch
        {
            "High" => $"High-tier profile ({s.LogicalCores} threads, {ramText}). Visual quality can stay maxed.",
            "Low" => $"Low-tier profile ({s.LogicalCores} threads, {ramText}). Reduced effects recommended for smoothness.",
            _ => $"Balanced profile ({s.LogicalCores} threads, {ramText}). Standard settings recommended.",
        };
    }

    private static bool ContainsAny(string haystack, params string[] needles) =>
        needles.Any(n => haystack.Contains(n, StringComparison.Ordinal));

    [SupportedOSPlatform("windows")]
    private static void TryWindowsCpu(ref string cpu)
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            foreach (var obj in s.Get())
            {
                cpu = obj["Name"]?.ToString()?.Trim() ?? cpu;
                break;
            }
        }
        catch { }
    }

    [SupportedOSPlatform("windows")]
    private static void TryWindowsRam(ref ulong ramBytes)
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
            foreach (var obj in s.Get())
            {
                var raw = obj["TotalPhysicalMemory"];
                if (raw is null) break;
                ramBytes = Convert.ToUInt64(raw, CultureInfo.InvariantCulture);
                break;
            }
        }
        catch { }
    }

    [SupportedOSPlatform("windows")]
    private static void TryWindowsGpu(ref string gpu)
    {
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var obj in s.Get())
            {
                gpu = obj["Name"]?.ToString()?.Trim() ?? gpu;
                break;
            }
        }
        catch { }
    }

    private static void TryLinuxCpu(ref string cpu)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (!line.StartsWith("model name", StringComparison.OrdinalIgnoreCase)) continue;
                var idx = line.IndexOf(':');
                if (idx >= 0) cpu = line[(idx + 1)..].Trim();
                break;
            }
        }
        catch { }
    }

    private static void TryLinuxRam(ref ulong ramBytes)
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (!line.StartsWith("MemTotal:", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && ulong.TryParse(parts[1], out var kb) && kb > 0)
                    ramBytes = kb * 1024;
                break;
            }
        }
        catch { }
    }
}
