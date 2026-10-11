using System.Reflection;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;
using PalworldPanel.Server.Workers;

namespace PalworldPanel.UnitTests;

public sealed class RuntimeMaintainabilityTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "palworldpanel-maintain-" + Guid.NewGuid().ToString("N"));
    public RuntimeMaintainabilityTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(root, true);
    }

    [Theory]
    [InlineData("Queued", "Queued", "Failed")]
    [InlineData("Running", "Preflight", "Failed")]
    [InlineData("Running", "Save", "Failed")]
    [InlineData("Running", "Stop", "NeedsAttention")]
    [InlineData("Running", "OfflineBackup", "NeedsAttention")]
    public void OneTaskFailureChoosesATerminalStateWithoutStoppingTheExecutor(string state, string phase, string terminal) =>
        Assert.Equal(terminal, TaskOutcome.TerminalState(state, phase));

    [Fact]
    public void ScheduledBackupRetriesOnlyFailedPreflightWithinTheSameDay()
    {
        var id = Guid.NewGuid().ToString("N");
        var day = "2026-10-10";
        var prefix = "schedule-" + id + "-" + day;
        Assert.Equal(prefix, BackupSchedule.NextKey(id, day, []));
        var failed = Attempt(prefix, "Failed", "Preflight");
        Assert.Equal(prefix + "-retry-2", BackupSchedule.NextKey(id, day, [failed]));
        var second = Attempt(prefix + "-retry-2", "Failed", "Preflight");
        Assert.Equal(prefix + "-retry-3", BackupSchedule.NextKey(id, day, [failed, second]));
        Assert.Null(BackupSchedule.NextKey(id, day, [failed, second, Attempt(prefix + "-retry-3", "Failed", "Preflight")]));
        Assert.Null(BackupSchedule.NextKey(id, day, [Attempt(prefix, "Succeeded", "Complete")]));
        Assert.Null(BackupSchedule.NextKey(id, day, [Attempt(prefix, "NeedsAttention", "Stop")]));
        Assert.Null(BackupSchedule.NextKey(id, day, [Attempt(prefix, "Cancelled", "Cancelled")]));
        Assert.Null(BackupSchedule.NextKey(id, day, [Attempt(prefix, "Failed", "Stop")]));
        Assert.Null(BackupSchedule.NextKey(id, day, [Attempt(prefix, "Running", "Preflight")]));
    }

    [Fact]
    public void ActiveTaskQueryExcludesFinishedWorkAndScheduleLookupStaysOnOneDay()
    {
        var store = new SqliteStore(Path.Combine(root, "panel.db"));
        var queued = store.Enqueue(new string('a', 32), "save", "{}", "admin", "idem-key-queued", "hash-queued");
        var finished = store.Enqueue(new string('b', 32), "save", "{}", "admin", "idem-key-failed", "hash-failed");
        Assert.True(store.SetTask(finished.Id, "Failed", "Preflight", "DockerUnavailable"));
        Assert.Equal([queued.Id], store.ActiveTasks().Select(task => task.Id));
        Assert.Equal([queued.Id], store.TasksInStates("Queued", "Running").Select(task => task.Id));

        var instance = Guid.NewGuid().ToString("N");
        var day = "2026-10-10";
        var prefix = "schedule-" + instance + "-" + day;
        var first = store.Enqueue(instance, "backup", "{}", "scheduler", prefix, "hash-schedule");
        store.SetTask(first.Id, "Failed", "Preflight", "DockerUnavailable");
        var retry = store.Enqueue(instance, "backup", "{}", "scheduler", prefix + "-retry-2", "hash-retry");
        store.SetTask(retry.Id, "Failed", "Preflight", "DockerUnavailable");
        store.Enqueue(new string('c', 32), "backup", "{}", "scheduler", "manual-backup-key", "hash-manual");
        store.Enqueue(new string('d', 32), "backup", "{}", "scheduler", prefix + "extra", "hash-extra");
        var attempts = store.SchedulerAttempts(instance, day);
        Assert.Equal([first.Id, retry.Id], attempts.Select(task => task.Id));
        Assert.Equal(prefix + "-retry-3", BackupSchedule.NextKey(instance, day, attempts));
        Assert.Equal("InvalidScheduleKey", Assert.Throws<PanelException>(() => store.SchedulerAttempts("not-a-guid", day)).Code);
    }

    [Fact]
    public async Task PoisonPayloadFailsThatTaskAndLeavesTheInstanceAvailable()
    {
        var store = new SqliteStore(Path.Combine(root, "poison.db"));
        var key = Path.Combine(root, "key");
        File.WriteAllBytes(key, RandomNumberGenerator.GetBytes(32));
        var image = "example/image@sha256:" + new string('a', 64);
        var options = new PanelOptions("192.168.1.2", 18080, ["192.168.1.3"], Path.Combine(root, "state"),
            [Path.Combine(root, "instances")], Path.Combine(root, "backups"), key, Path.Combine(root, "admin"),
            "", "", image, [image], AllowHttp: true);
        var vault = new SecretVault(key);
        var docker = new DockerBackend();
        var instances = new InstanceService(options, store, vault, docker, new GameRestClient(vault), new HostMetrics());
        using var http = new HttpClient();
        var logger = new CollectingLogger();
        var worker = new TaskWorker(store, instances, docker, new GameRestClient(vault), new BackupService(options, store, instances),
            new QuarantineService(options, store, instances), new UnusedLifetime(), new HeavyIoGate(),
            new GameUpdateService(new SteamReleaseCatalog(http), vault), logger);
        var instanceId = Guid.NewGuid().ToString("N");
        store.SaveInstance(new InstanceRecord(instanceId, "synthetic", root, "project", "service", image, 18211, 18312, 18415,
            "", "", new GameRules("synthetic")));
        var task = store.Enqueue(instanceId, "save", "{", "admin", "poison-idempotency-key", "hash-poison");
        var run = typeof(TaskWorker).GetMethod("RunAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)run.Invoke(worker, [task, CancellationToken.None])!;
        var failed = store.Task(task.Id);
        Assert.Equal("Failed", failed.State);
        Assert.Equal("ExecutorFault", failed.SafeCode);
        Assert.Equal("任务未能由执行器完成，已停止自动继续。", failed.Message);
        Assert.DoesNotContain("JsonException", failed.Message);
        Assert.Contains(task.Id, logger.Text);
        Assert.Contains("JsonReaderException", logger.Text);
        Assert.Contains("ExecutorFault", logger.Text);
        var next = store.Enqueue(instanceId, "save", "{}", "admin", "after-poison-key", "hash-next");
        Assert.Equal("Queued", next.State);
    }

    [Fact]
    public void DiagnosticDetailRedactsSecretsAndStaysBounded()
    {
        var detail = LogRedactor.Detail("ADMIN_PASSWORD=synthetic-secret\n" + new string('x', 5000));
        Assert.DoesNotContain("synthetic-secret", detail);
        Assert.Contains("[redacted sensitive field]", detail);
        Assert.True(detail.Length <= 4000);
    }

    [Fact]
    public void AnonymousDashboardDropsAddressesFromObservations()
    {
        using var value = System.Text.Json.JsonDocument.Parse("""
            {"container":"running","players":2,"gameAddress":"192.168.1.20","worldGuid":"abc","publishedUdp":[{"address":"10.0.0.8","port":"8211"}]}
            """);
        var projected = System.Text.Json.JsonSerializer.Serialize(PublicDashboard.Project(value.RootElement), DurableFile.JsonCompact);
        Assert.DoesNotContain("192.168.1.20", projected);
        Assert.DoesNotContain("worldGuid", projected);
        Assert.DoesNotContain("10.0.0.8", projected);
        Assert.Contains("\"players\":2", projected);
    }

    private static TaskRecord Attempt(string key, string state, string phase) =>
        new("task", "instance", "backup", state, phase, "{}", "scheduler", key, "hash", "2026-10-10T21:00:00Z");

    private sealed class UnusedLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private sealed class CollectingLogger : ILogger<TaskWorker>
    {
        public string Text = "";
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Text += formatter(state, exception);
    }
}
