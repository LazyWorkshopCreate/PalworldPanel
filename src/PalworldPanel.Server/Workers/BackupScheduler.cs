using System.Globalization;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Workers;

public sealed class BackupScheduler(SqliteStore store, IHostApplicationLifetime lifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StartupGate.WaitAsync(lifetime, stoppingToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        while (!stoppingToken.IsCancellationRequested)
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
            foreach (var instance in store.Instances().Where(i => i.Writable && i.SourceHash is not null))
            {
                if (!TimeOnly.TryParseExact(instance.BackupTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var scheduled)) continue;
                var now = TimeOnly.FromDateTime(local.DateTime);
                if (now < scheduled) continue;
                var day = local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var key = BackupSchedule.NextKey(instance.Id, day, store.SchedulerAttempts(instance.Id, day));
                if (key is null) continue;
                try { store.Enqueue(instance.Id, "backup", "{}", "scheduler", key, InstanceService.Hash(key)); }
                catch (PanelException error) when (error.Code is "TaskConflict" or "IdempotencyConflict") { /* No overlapping or destructive retry. */ }
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
