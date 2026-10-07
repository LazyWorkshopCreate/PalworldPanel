using System.Globalization;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed record RuntimeMemorySample(long TotalBytes, long AvailableBytes)
{
    public long UsedBytes => TotalBytes - AvailableBytes;
}

public static class RuntimeMemory
{
    public static long PendingMiB(IEnumerable<TaskRecord> tasks, IEnumerable<InstanceRecord> instances, string? excludedInstance)
    {
        var pending = tasks.Where(task => task.State is "Queued" or "Running" && task.Kind is "create" or "start" && task.InstanceId != excludedInstance)
            .Select(task => task.InstanceId).ToHashSet(StringComparer.Ordinal);
        return instances.Where(instance => pending.Contains(instance.Id)).Sum(instance => instance.Desired.MemoryMiB);
    }

    public static RuntimeMemorySample Parse(string text)
    {
        var values = new Dictionary<string, long>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split(':', 2);
            if (parts.Length != 2 || parts[0] is not ("MemTotal" or "MemAvailable")) continue;
            var fields = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length != 2 || fields[1] != "kB" || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value > long.MaxValue / 1024 || !values.TryAdd(parts[0], value * 1024))
                throw new PanelException("MemorySampleUnavailable", "实时内存数据无效，请稍后重试。", 503);
        }
        if (!values.TryGetValue("MemTotal", out var total) || !values.TryGetValue("MemAvailable", out var available) || total <= 0 || available < 0 || available > total)
            throw new PanelException("MemorySampleUnavailable", "无法读取实时可用内存，请稍后重试。", 503);
        return new(total, available);
    }

    public static void Check(long availableBytes, long requestedMiB, long reservedMiB, long pendingMiB)
    {
        var usableMiB = Math.Max(0, availableBytes / 1024 / 1024 - reservedMiB - pendingMiB);
        if (requestedMiB > usableMiB)
            throw new PanelException("ResourceBudgetExceeded", $"可用内存不足：本次需要 {requestedMiB / 1024d:F1} GiB，扣除系统预留和等待启动任务后可用 {usableMiB / 1024d:F1} GiB。", 409);
    }

    public static async Task<RuntimeMemorySample> ReadAsync(DockerBackend docker, PanelOptions options, CancellationToken cancellation)
    {
        var running = await docker.RunAsync(["ps", "-q", "--filter", "ancestor=" + options.DefaultImage], TimeSpan.FromSeconds(10), cancellation: cancellation);
        foreach (var container in running.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3))
        {
            var sample = await docker.RunAsync(["exec", container.Trim(), "/bin/cat", "/proc/meminfo"], TimeSpan.FromSeconds(10), cancellation: cancellation);
            if (sample.ExitCode == 0) return Parse(sample.Output);
        }
        // /proc/meminfo reports the Linux engine's actual free memory, including reclaimable cache.
        // A read-only probe also works when every game container is stopped. Never pull an image here.
        var name = "pp-memory-" + Guid.NewGuid().ToString("N");
        CommandResult result;
        try
        {
            result = await docker.RunAsync(["run", "--rm", "--pull", "never", "--name", name, "--network", "none", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges",
                "--memory", "32m", "--cpus", "0.1", "--entrypoint", "/bin/cat", options.DefaultImage, "/proc/meminfo"],
                TimeSpan.FromSeconds(15), cancellation: cancellation);
        }
        catch
        {
            await docker.RunAsync(["rm", "--force", name], TimeSpan.FromSeconds(10), cancellation: CancellationToken.None);
            throw;
        }
        if (result.ExitCode != 0) throw new PanelException("MemorySampleUnavailable", "无法读取游戏运行环境的可用内存，请确认本地游戏镜像已准备好。", 503);
        return Parse(result.Output);
    }
}
