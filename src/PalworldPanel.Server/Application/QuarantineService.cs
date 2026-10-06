using System.Text.Json;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed record QuarantineRecord(string Id, string OriginalRoot, string BackupId, DateTimeOffset Utc);

public sealed class QuarantineService(PanelOptions options, SqliteStore store, InstanceService instances)
{
    public InstanceRecord ReconcileInterruptedMove(InstanceRecord instance, TaskRecord task)
    {
        if (task.Kind != "purge" || !instance.Owned || Directory.Exists(instance.Root) || instance.QuarantinedUtc is not null) return instance;
        var root = options.InstanceRoots.SingleOrDefault(allowed => SafePaths.Within(allowed, instance.Id) == instance.Root)
            ?? throw new PanelException("QuarantinePathChanged", "无法核实原目录归属。", 409);
        var destination = SafePaths.Within(root, ".quarantine/" + instance.Id);
        var record = JsonSerializer.Deserialize<QuarantineRecord>(File.ReadAllText(SafePaths.Within(destination, "quarantine.json")), DurableFile.Json)
            ?? throw new PanelException("QuarantineCorrupt", "隔离清单无效。", 409);
        if (record.Id != instance.Id || record.OriginalRoot != instance.Root || record.BackupId != task.RecoveryPoint)
            throw new PanelException("QuarantineCorrupt", "隔离现场与任务不匹配。", 409);
        var moved = instance with {Root = destination, QuarantinedUtc = record.Utc.ToString("O"), Writable = false, DesiredPower = "stopped", ContainerId = null};
        store.SaveInstance(moved);
        return moved;
    }

    public InstanceRecord Undo(InstanceRecord instance)
    {
        var record = Read(instance);
        if (Directory.Exists(record.OriginalRoot)) throw new PanelException("OriginalRootOccupied", "原实例目录已被占用，拒绝覆盖。", 409);
        Directory.Move(instance.Root, record.OriginalRoot);
        DurableFile.FlushDirectory(Path.GetDirectoryName(record.OriginalRoot)!);
        var restored = instance with { Root = record.OriginalRoot, Writable = true, QuarantinedUtc = null,
            PurgedUtc = null, DesiredPower = "stopped", Revision = instance.Revision + 1 };
        store.SaveInstance(restored with { SourceHash = instances.SourceHash(restored) });
        return restored;
    }

    public async Task FinalizeAsync(InstanceRecord instance, CancellationToken ct)
    {
        var record = Read(instance);
        if (DateTimeOffset.UtcNow - record.Utc < TimeSpan.FromDays(7))
            throw new PanelException("QuarantineRetention", "隔离目录必须保留至少 7 天。", 409);
        var backup = store.Backups(instance.Id).SingleOrDefault(b => b.Id == record.BackupId && b.Protected)
            ?? throw new PanelException("FinalBackupMissing", "独立最终备份不可用，拒绝永久清理。", 409);
        if (await ZipWorldArchive.HashAsync(Path.Combine(backup.Path, "manifest.json"), ct) != backup.Sha256)
            throw new PanelException("BackupCorrupt", "最终备份清单校验失败。");
        await BackupService.VerifyAsync(backup.Path, ct);
        foreach (var file in TreeFiles.Files(instance.Root)) SafePaths.RejectLinks(file);
        Directory.Delete(instance.Root, true);
        DurableFile.FlushDirectory(Path.GetDirectoryName(instance.Root)!);
        store.SaveInstance(instance with { PurgedUtc = DateTimeOffset.UtcNow.ToString("O"), Revision = instance.Revision + 1,
            SourceHash = null, Writable = false, DesiredPower = "stopped" });
    }

    private QuarantineRecord Read(InstanceRecord instance)
    {
        if (!instance.Owned || instance.QuarantinedUtc is null || instance.PurgedUtc is not null)
            throw new PanelException("QuarantineUnavailable", "仅本项目创建且未清空的隔离目录支持此操作。", 409);
        var matches = options.InstanceRoots.Where(root => SafePaths.Within(root, ".quarantine/" + instance.Id) == instance.Root).ToArray();
        if (matches.Length != 1) throw new PanelException("QuarantinePathChanged", "隔离目录归属无法确认。", 409);
        var record = JsonSerializer.Deserialize<QuarantineRecord>(File.ReadAllText(SafePaths.Within(instance.Root, "quarantine.json")), DurableFile.Json)
            ?? throw new PanelException("QuarantineCorrupt", "隔离清单无效。");
        if (record.Id != instance.Id || SafePaths.Within(matches[0], instance.Id) != record.OriginalRoot ||
            record.Utc != DateTimeOffset.Parse(instance.QuarantinedUtc))
            throw new PanelException("QuarantineCorrupt", "隔离清单与实例记录不一致。");
        return record;
    }
}
