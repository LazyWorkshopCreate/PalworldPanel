using System.Security.Cryptography;
using System.Text.Json;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed record BackupFile(string Name, long Bytes, string Sha256);
public sealed record BackupManifest(string Schema, InstanceRecord Instance, string Consistency, string CreatedUtc,
    bool ContainsInstallation, BackupFile[] Files);

public sealed class BackupService(PanelOptions options, SqliteStore store, InstanceService instances)
{
    public void Prune(InstanceRecord instance)
    {
        var records = store.Backups(instance.Id).OrderByDescending(b => b.CreatedUtc, StringComparer.Ordinal).ToArray();
        var protectedIds = store.Tasks("Running").Concat(store.Tasks("Queued")).Concat(store.Tasks("NeedsAttention"))
            .Select(t => t.RecoveryPoint).ToHashSet();
        foreach (var record in records.Skip(1).Where(b => !b.Protected && !protectedIds.Contains(b.Id) &&
            DateTimeOffset.Parse(b.CreatedUtc) < DateTimeOffset.UtcNow.AddDays(-instance.RetentionDays)))
        {
            var path = SafePaths.Within(options.BackupRoot, instance.Id + "/" + record.Id);
            if (path != Path.GetFullPath(record.Path)) throw new PanelException("BackupPathChanged", "备份目录归属变化，拒绝清理。");
            SafePaths.RejectLinks(path);
            foreach (var file in TreeFiles.Files(path)) SafePaths.RejectLinks(file);
            Directory.Delete(path, true);
            store.ForgetBackup(record.Id);
        }
    }
    public async Task<BackupRecord> CreateAsync(InstanceRecord instance, string purpose, bool installation,
        CancellationToken cancellation = default)
    {
        var data = SafePaths.Within(instance.Root, "data");
        var saved = SafePaths.Within(data, "Pal/Saved");
        if (!Directory.Exists(saved)) throw new PanelException("SavedMissing", "世界目录不存在，不能建立恢复点。", 409);
        if (instance.WorldGuid is null || !System.Text.RegularExpressions.Regex.IsMatch(instance.WorldGuid, "^[a-fA-F0-9]{32}$"))
            throw new PanelException("UnknownWorld", "世界身份未经核实，不能声明可信恢复点。", 409);
        var worldDirectory = WorldFiles.Find(saved, instance.WorldGuid);
        foreach (var name in new[] { "Level.sav", "LevelMeta.sav" })
        {
            var file = SafePaths.Within(worldDirectory, name);
            if (!File.Exists(file) || new FileInfo(file).Length == 0) throw new PanelException("WorldFilesMissing", "当前世界未形成完整持久存档，拒绝建立可信恢复点。", 409);
        }
        var required = TreeFiles.Bytes(installation ? data : saved);
        instances.CheckDisk(options.BackupRoot, required);
        var id = Guid.NewGuid().ToString("N");
        var parent = SafePaths.Within(options.BackupRoot, instance.Id);
        Directory.CreateDirectory(parent);
        var partial = SafePaths.Within(parent, id + ".partial");
        var complete = SafePaths.Within(parent, id);
        Directory.CreateDirectory(partial);
        TreeFiles.Copy(installation ? data : saved, Path.Combine(partial, installation ? "installation" : "Saved"));
        foreach (var file in new[] { "compose.yaml", "settings.env", "secrets.env" })
            DurableFile.Write(Path.Combine(partial, file), File.ReadAllBytes(SafePaths.Within(instance.Root, file)));
        var files = new List<BackupFile>();
        foreach (var file in TreeFiles.Files(partial))
            files.Add(new BackupFile(Path.GetRelativePath(partial, file).Replace('\\', '/'), new FileInfo(file).Length,
                await ZipWorldArchive.HashAsync(file, cancellation)));
        var manifest = new BackupManifest("palworldpanel-backup-1", instance, "offline", DateTimeOffset.UtcNow.ToString("O"), installation, files.ToArray());
        DurableFile.WriteJson(Path.Combine(partial, "manifest.json"), manifest);
        await VerifyAsync(partial, cancellation);
        Directory.Move(partial, complete);
        DurableFile.FlushDirectory(parent);
        var record = new BackupRecord(id, instance.Id, complete,
            await ZipWorldArchive.HashAsync(Path.Combine(complete, "manifest.json"), cancellation), manifest.CreatedUtc,
            files.Sum(f => f.Bytes), instance.WorldGuid, instance.GameBuild, instance.Image,
            purpose != "manual" && purpose != "scheduled", installation, purpose);
        store.SaveBackup(record);
        return record;
    }

    public static async Task<BackupManifest> VerifyAsync(string path, CancellationToken cancellation = default)
    {
        SafePaths.RejectLinks(path);
        var manifest = JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(Path.Combine(path, "manifest.json")), DurableFile.Json)
            ?? throw new PanelException("InvalidBackup", "备份清单不存在或无效。");
        if (manifest.Schema != "palworldpanel-backup-1" || manifest.Consistency != "offline")
            throw new PanelException("InvalidBackup", "备份格式或一致性等级不受支持。");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            var actual = SafePaths.Within(path, file.Name);
            if (!seen.Add(file.Name) || !File.Exists(actual) || new FileInfo(actual).Length != file.Bytes ||
                await ZipWorldArchive.HashAsync(actual, cancellation) != file.Sha256)
                throw new PanelException("BackupCorrupt", "备份文件校验失败，拒绝恢复。");
        }
        if (TreeFiles.Files(path).Count() != manifest.Files.Length + 1) throw new PanelException("BackupCorrupt", "备份包含未知文件，拒绝恢复。");
        return manifest;
    }
}
