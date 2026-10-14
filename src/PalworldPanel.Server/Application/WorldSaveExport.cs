using System.IO.Compression;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed class WorldSaveExport(PanelOptions options, SqliteStore store, InstanceService instances, HeavyIoGate ioGate)
{
    public static IEnumerable<string> SaveFilesOnly(string world) => TreeFiles.Files(world).Where(f => System.IO.Path.GetExtension(f).Equals(".sav", StringComparison.OrdinalIgnoreCase));
    public async Task<FileStream> ExportAsync(InstanceRecord instance, string backupId, CancellationToken ct)
    {
        instances.CheckRoot(instance);
        using var instanceLock = DiskLock.AcquireInstance(options.StateRoot, instance.Root, instance.Id);
        instance = store.Instance(instance.Id);
        if (!WorldReadOnlySecurity.Eligible(instance)) throw new PanelException("WorldAccessDenied", "实例不可用。", 401);
        if (store.ActiveTasks().Any(t => t.InstanceId == instance.Id)) throw new PanelException("TaskConflict", "实例有未结束任务，请稍后下载。", 409);
        var record = store.Backups(instance.Id).SingleOrDefault(b => b.Id == backupId && WorldReadOnlySecurity.Normalize(b.WorldGuid) == WorldReadOnlySecurity.Normalize(instance.WorldGuid))
            ?? throw new PanelException("BackupNotFound", "该世界备份不存在。", 404);
        var path = SafePaths.Within(options.BackupRoot, instance.Id + "/" + record.Id);
        if (path != System.IO.Path.GetFullPath(record.Path)) throw new PanelException("BackupPathChanged", "备份归属变化，拒绝读取。", 409);
        await ioGate.Semaphore.WaitAsync(ct);
        string? temporary = null;
        try
        {
            if (await ZipWorldArchive.HashAsync(SafePaths.Within(path, "manifest.json"), ct) != record.Sha256) throw new PanelException("BackupCorrupt", "备份清单校验失败。", 409);
            var manifest = await BackupService.VerifyAsync(path, ct);
            if (manifest.Instance.Id != instance.Id || WorldReadOnlySecurity.Normalize(manifest.Instance.WorldGuid) != WorldReadOnlySecurity.Normalize(instance.WorldGuid) || manifest.ContainsInstallation != record.ContainsInstallation)
                throw new PanelException("BackupCorrupt", "备份世界归属不一致。", 409);
            var saved = SafePaths.Within(path, record.ContainsInstallation ? "installation/Pal/Saved" : "Saved");
            var world = WorldFiles.Find(saved, instance.WorldGuid!);
            var files = SaveFilesOnly(world).Take(100001).ToArray();
            var bytes = files.Sum(f => new FileInfo(f).Length);
            if (files.Length is 0 or > 100000 || bytes > 20L << 30) throw new PanelException("ArchiveLimit", "世界存档为空或超出下载限制。", 409);
            var verifiedNames = manifest.Files.Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var file in files)
                if (!verifiedNames.Contains(System.IO.Path.GetRelativePath(path, file).Replace('\\', '/')))
                    throw new PanelException("BackupCorrupt", "世界存档不在已校验清单中。", 409);
            var directory = SafePaths.Within(options.StateRoot, "exports");
            Directory.CreateDirectory(directory);
            instances.CheckDisk(directory, checked(bytes * 2));
            temporary = SafePaths.Within(directory, Guid.NewGuid().ToString("N") + ".world.zip.partial");
            await using (var stream = DurableFile.CreatePrivateFile(temporary))
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    foreach (var file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = zip.CreateEntry("Saved/SaveGames/0/" + instance.WorldGuid + "/" + System.IO.Path.GetRelativePath(world, file).Replace('\\', '/'), CompressionLevel.Fastest);
                        await using var target = entry.Open();
                        await using var source = File.OpenRead(file);
                        await source.CopyToAsync(target, ct);
                    }
                stream.Flush(true);
            }
            return new FileStream(temporary, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        }
        catch { if (temporary is not null) File.Delete(temporary); throw; }
        finally { ioGate.Semaphore.Release(); }
    }
}
