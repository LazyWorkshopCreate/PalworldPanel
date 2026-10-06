using System.IO.Compression;
using System.Text.Json;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed class PanelBackupService(PanelOptions options, SqliteStore store, HeavyIoGate ioGate)
{
    public async Task<FileStream> ExportAsync(string passphrase, CancellationToken ct)
    {
        EncryptedArchive.ValidatePassphrase(passphrase);
        using var allocationLock = DiskLock.Acquire(Path.Combine(options.StateRoot, "allocation.lock"));
        var locks = new List<DiskLock>();
        var temporary = SafePaths.Within(options.StateRoot, "panel-exports/" + Guid.NewGuid().ToString("N"));
        var encrypted = temporary + ".ppbak";
        var ioAcquired = false;
        try
        {
            foreach (var instance in store.Instances().OrderBy(i => i.Id, StringComparer.Ordinal))
            {
                var useRoot = CanReadRoot(instance);
                locks.Add(DiskLock.AcquireInstance(options.StateRoot, instance.Root, instance.Id, requireRoot: useRoot));
            }
            if (store.Tasks("Running").Count != 0) throw new PanelException("UnsafeCheckpoint", "面板有执行中任务，稍后再建立灾备。", 409);
            await ioGate.Semaphore.WaitAsync(ct);
            ioAcquired = true;
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(temporary);
            else Directory.CreateDirectory(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            store.BackupDatabase(Path.Combine(temporary, "panel.db"));
            var material = new Dictionary<string, string> { ["master-key"] = options.KeyFile,
                ["administrator.json"] = options.AdministratorFile };
            if (File.Exists(options.CertificateFile) && File.Exists(options.CertificatePasswordFile))
            {
                material["tls.pfx"] = options.CertificateFile;
                material["tls-password"] = options.CertificatePasswordFile;
            }
            foreach (var file in material) DurableFile.Write(Path.Combine(temporary, "private", file.Key), File.ReadAllBytes(file.Value));
            DurableFile.WriteJson(Path.Combine(temporary, "panel-options.json"), options);
            var keys = SafePaths.Within(options.StateRoot, "session-keys");
            if (Directory.Exists(keys)) TreeFiles.Copy(keys, Path.Combine(temporary, "session-keys"));
            foreach (var instance in store.Instances().Where(CanReadRoot))
            {
                foreach (var journal in Directory.EnumerateFiles(instance.Root, "swap.*.json")
                    .Concat(Directory.Exists(Path.Combine(instance.Root, "data", "Pal"))
                        ? Directory.EnumerateFiles(Path.Combine(instance.Root, "data", "Pal"), "swap.*.json") : []))
                {
                    SafePaths.RejectLinks(journal);
                    DurableFile.Write(Path.Combine(temporary, "journals", instance.Id, Path.GetFileName(journal)), File.ReadAllBytes(journal));
                }
            }
            DurableFile.WriteJson(Path.Combine(temporary, "disaster-manifest.json"), new
            { schema = "palworldpanel-disaster-1", createdUtc = DateTimeOffset.UtcNow,
                includesGameData = false, restoreRequiresTaskReview = true,
                latestBackups = store.Instances().Select(i => new { i.Id, latest = store.Backups(i.Id).OrderByDescending(b => b.CreatedUtc).FirstOrDefault()?.Id }) });
            if (TreeFiles.Bytes(temporary) > (128L << 20)) throw new PanelException("PanelBackupTooLarge", "面板元数据超出灾备上限。", 413);
            using var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
                foreach (var file in TreeFiles.Files(temporary))
                {
                    if (zip.Length > (128L << 20)) throw new PanelException("PanelBackupTooLarge", "面板元数据超出灾备上限。", 413);
                    var entry = archive.CreateEntry(Path.GetRelativePath(temporary, file).Replace('\\', '/'), CompressionLevel.Fastest);
                    await using var target = entry.Open();
                    await using var source = File.OpenRead(file);
                    await source.CopyToAsync(target, ct);
                }
            zip.Position = 0;
            await using (var output = DurableFile.CreatePrivateFile(encrypted))
            { await EncryptedArchive.EncryptAsync(zip, output, passphrase, ct); output.Flush(true); }
            var result = new FileStream(encrypted, FileMode.Open, FileAccess.Read, FileShare.Read, 81920,
                FileOptions.Asynchronous | FileOptions.DeleteOnClose);
            Directory.Delete(temporary, true);
            return result;
        }
        catch
        {
            File.Delete(encrypted);
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            throw;
        }
        finally { if (ioAcquired) ioGate.Semaphore.Release(); foreach (var instanceLock in locks) instanceLock.Dispose(); }
    }
    private bool CanReadRoot(InstanceRecord instance)
    {
        if (instance.PurgedUtc is not null || !Directory.Exists(instance.Root)) return false;
        try { SafePaths.EnsureApproved(instance.Root, options.InstanceRoots); return true; }
        catch (PanelException) { return false; }
    }
}
