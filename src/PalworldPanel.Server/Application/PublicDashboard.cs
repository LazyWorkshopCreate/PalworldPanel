using System.Text.Json;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed record PublicObservation(string Container = "unknown", int? Players = null, double? Fps = null,
    double? Cpu = null, long? MemoryBytes = null, DateTimeOffset? UpdatedUtc = null, bool Stale = true);
public sealed record PublicInstance(string Id, string Name, PublicObservation Status);
public sealed record PublicDashboardSnapshot(HostSample Host, PublicInstance[] Instances, DateTimeOffset UpdatedUtc);

public sealed class PublicDashboard(SqliteStore store, InstanceService instances, HostMetrics metrics)
{
    private readonly SemaphoreSlim gate = new(1);
    private PublicDashboardSnapshot? cached;
    public async Task<PublicDashboardSnapshot> ReadAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (cached is not null && DateTimeOffset.UtcNow - cached.UpdatedUtc < TimeSpan.FromSeconds(5)) return cached;
            var registered = store.Instances().Where(i => i.QuarantinedUtc is null).ToArray();
            var result = registered.Select(i => new PublicInstance(i.Id, i.Name, new())).ToArray();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(8));
            try
            {
                await Parallel.ForEachAsync(Enumerable.Range(0, registered.Length), new ParallelOptions
                { MaxDegreeOfParallelism = 4, CancellationToken = deadline.Token }, async (index, token) =>
                {
                    var value = JsonSerializer.SerializeToElement(await instances.ObserveAsync(registered[index], token), DurableFile.Json);
                    result[index] = result[index] with { Status = Project(value) };
                });
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            return cached = new(metrics.Sample(), result, DateTimeOffset.UtcNow);
        }
        finally { gate.Release(); }
    }
    // Explicit allowlist DTO: never return full instance/observation/config objects anonymously.
    public static PublicObservation Project(JsonElement value) => value.Deserialize<PublicObservation>(DurableFile.Json) ?? new();
}
