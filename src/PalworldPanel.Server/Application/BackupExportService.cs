using System.IO.Compression;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed class BackupExportService(PanelOptions options, SqliteStore store, InstanceService instances, HeavyIoGate ioGate)
{
    public async Task<FileStream> ExportAsync(string instanceId, string backupId, string passphrase, CancellationToken ct)
    {
        EncryptedArchive.ValidatePassphrase(passphrase);
        var record = store.Backups(instanceId).SingleOrDefault(b => b.Id == backupId)
            ?? throw new PanelException("BackupNotFound", "恢复点不存在。", 404);
        var instance = store.Instances().SingleOrDefault(i => i.Id == instanceId);
        var useRoot = instance is not null && instance.PurgedUtc is null && Directory.Exists(instance.Root);
        if (useRoot) { try { instances.CheckRoot(instance!); } catch (PanelException) { useRoot = false; } }
        using var instanceLock = DiskLock.AcquireInstance(options.StateRoot, instance?.Root ?? record.Path, instanceId, requireRoot: useRoot);
        if (SafePaths.Within(options.BackupRoot, record.InstanceId + "/" + record.Id) != Path.GetFullPath(record.Path))
            throw new PanelException("BackupPathChanged", "恢复点目录归属发生变化，拒绝读取。");
        if (await ZipWorldArchive.HashAsync(Path.Combine(record.Path, "manifest.json"), ct) != record.Sha256)
            throw new PanelException("BackupCorrupt", "恢复清单校验失败。");
        await BackupService.VerifyAsync(record.Path, ct);
        var directory = SafePaths.Within(options.StateRoot, "exports");
        Directory.CreateDirectory(directory);
        instances.CheckDisk(directory, checked(record.Bytes * 2));
        var stem = SafePaths.Within(directory, Guid.NewGuid().ToString("N"));
        var zip = stem + ".zip.partial";
        var encrypted = stem + ".ppbak.partial";
        await ioGate.Semaphore.WaitAsync(ct);
        try
        {
            using (var stream = new FileStream(zip, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                Private(zip);
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    foreach (var file in TreeFiles.Files(record.Path))
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = archive.CreateEntry(Path.GetRelativePath(record.Path, file).Replace('\\', '/'), CompressionLevel.Fastest);
                        if (!OperatingSystem.IsWindows()) entry.ExternalAttributes = (0x8000 | (int)File.GetUnixFileMode(file)) << 16;
                        await using var target = entry.Open();
                        await using var source = File.OpenRead(file);
                        await source.CopyToAsync(target, ct);
                    }
                stream.Flush(true);
            }
            await using (var source = File.OpenRead(zip))
            await using (var target = new FileStream(encrypted, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                Private(encrypted);
                await EncryptedArchive.EncryptAsync(source, target, passphrase, ct);
                target.Flush(true);
            }
            var result = new FileStream(encrypted, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            File.Delete(zip);
            return result;
        }
        catch { File.Delete(zip); File.Delete(encrypted); throw; }
        finally { ioGate.Semaphore.Release(); }
    }
    private static void Private(string path)
    { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
}
