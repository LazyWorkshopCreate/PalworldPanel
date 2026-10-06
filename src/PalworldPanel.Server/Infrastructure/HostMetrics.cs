using System.Globalization;

namespace PalworldPanel.Server.Infrastructure;

public sealed record HostSample(long? UsedMemoryBytes, long? AvailableMemoryBytes, double? CpuPercent, DateTimeOffset UpdatedUtc,
    long? TotalMemoryBytes = null, int? CpuCount = null);
public sealed class HostMetrics
{
    private readonly object gate = new();
    private HostSample? last;
    private (long Total, long Idle)? counters;
    public HostSample Sample()
    {
        lock (gate)
        {
            if (last is not null && DateTimeOffset.UtcNow - last.UpdatedUtc < TimeSpan.FromSeconds(5)) return last;
            long? used = null, available = null;
            long? totalMemory = null;
            double? cpu = null;
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    var memory = File.ReadLines("/proc/meminfo").Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        .Where(fields => fields.Length >= 2).ToDictionary(fields => fields[0].TrimEnd(':'), fields => long.Parse(fields[1], CultureInfo.InvariantCulture) * 1024);
                    if (memory.TryGetValue("MemTotal", out var total) && memory.TryGetValue("MemAvailable", out var free) && free >= 0 && free <= total)
                    { used = total - free; available = free; totalMemory = total; }
                    var ticks = File.ReadLines("/proc/stat").First().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Skip(1).Take(8).Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                    var current = (Total: ticks.Sum(), Idle: ticks[3] + ticks[4]);
                    if (counters is { } previous && current.Total > previous.Total && current.Idle >= previous.Idle)
                        cpu = Math.Clamp(100d * (1 - (double)(current.Idle - previous.Idle) / (current.Total - previous.Total)), 0, 100);
                    counters = current;
                }
                catch (Exception error) when (error is IOException or FormatException or IndexOutOfRangeException) { }
            }
            if (OperatingSystem.IsWindows())
            {
                var measured = WindowsMetrics.Read();
                totalMemory = measured.Total; available = measured.Available;
                used = totalMemory - available;
                if (measured.Ticks is { } total && measured.Idle is { } idle)
                {
                    if (counters is { } previous && total > previous.Total && idle >= previous.Idle)
                        cpu = Math.Clamp(100d * (1 - (double)(idle - previous.Idle) / (total - previous.Total)), 0, 100);
                    counters = (total, idle);
                }
            }
            return last = new(used, available, cpu, DateTimeOffset.UtcNow, totalMemory, Environment.ProcessorCount);
        }
    }
}
