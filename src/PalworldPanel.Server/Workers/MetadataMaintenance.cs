using System.Text.Json;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Workers;

public sealed class MetadataMaintenance(PanelOptions options, SqliteStore store, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StartupGate.WaitAsync(lifetime, stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            CleanUploads();
            store.PruneAudit(DateTimeOffset.UtcNow.AddDays(-30));
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private void CleanUploads()
    {
        var root = SafePaths.Within(options.StateRoot, "uploads");
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(directory);
            if (!Guid.TryParseExact(id, "N", out _)) continue;
            try
            {
                SafePaths.RejectLinks(directory);
                var instance = store.Instance(id);
                using var instanceLock = DiskLock.AcquireInstance(options.StateRoot, instance.Root, instance.Id);
                using var uploadLock = DiskLock.Acquire(Path.Combine(directory, "upload.lock"), createParent: false);
                var active = store.Tasks("Queued").Concat(store.Tasks("Running")).Concat(store.Tasks("NeedsAttention")).ToArray();
                var referenced = new HashSet<string>(StringComparer.Ordinal);
                foreach (var task in active.Where(task => task.InstanceId == id))
                {
                    using var payload = JsonDocument.Parse(task.Payload);
                    if (payload.RootElement.TryGetProperty("uploadId", out var upload)) referenced.Add(upload.GetString() ?? "");
                }
                foreach (var file in Directory.EnumerateFiles(directory, "*.zip"))
                {
                    SafePaths.RejectLinks(file);
                    var uploadId = Path.GetFileNameWithoutExtension(file);
                    if (Guid.TryParseExact(uploadId, "N", out _) && !referenced.Contains(uploadId) &&
                        File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddHours(-24)) File.Delete(file);
                }
            }
            catch (Exception error) when (error is PanelException or IOException or JsonException)
            { /* Busy, unrecognized or unsafe evidence is retained. */ }
        }
    }
}
