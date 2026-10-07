using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed class TaskRecoveryService(SqliteStore store, InstanceService instances, DockerBackend docker,
    GameRestClient game, BackupService backups, HeavyIoGate ioGate, QuarantineService quarantine)
{
    public async Task<TaskRecord> RecoverAsync(string id, string resolution, bool playersVerified, string user)
    {
        var task = store.Task(id);
        if (task.State != "NeedsAttention") throw new PanelException("RecoveryStateChanged", "任务不处于待处理状态。", 409);
        var instance = store.Instance(task.InstanceId);
        instances.CheckRoot(instance);
        using var instanceLock = DiskLock.AcquireInstance(instances.Options.StateRoot, instance.Root, instance.Id);
        if (store.Task(id).State != "NeedsAttention") throw new PanelException("RecoveryStateChanged", "任务已经变化。", 409);
        if (task.Kind == "upgrade") await docker.StopUpdaterAsync(task.Id);
        if (resolution == "start-verification")
        {
            if (task.SafeCode != "PlayerVerificationPending") throw new PanelException("RecoveryRequired", "该任务不能直接启动验收。", 409);
            instances.CheckSource(instance);
            await docker.ComposeAsync(instance, ["up", "--detach", "--no-deps", "--pull", "never", instance.Service], true);
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(30));
                while (true)
                {
                    try
                    {
                        var info = await game.ReadAsync(instance, "info", deadline.Token);
                        if (info.GetProperty("worldguid").GetString() != instance.WorldGuid || info.GetProperty("version").GetString() != instance.GameBuild)
                            throw new PanelException("VerificationMismatch", "世界或游戏版本与预期不一致。", 409);
                        if (!ConfigurationObservation.Matches(await game.ReadAsync(instance, "settings", deadline.Token), instance.Applied ?? instance.Desired))
                            throw new PanelException("SettingsMismatch", "实际规则与应用配置不一致。", 409);
                        await game.ReadAsync(instance, "metrics", deadline.Token);
                        break;
                    }
                    catch (HttpRequestException) { }
                    catch (PanelException error) when (error.Code == "GameApiUnavailable") { }
                    catch (TaskCanceledException) when (!deadline.IsCancellationRequested) { }
                    await Task.Delay(TimeSpan.FromSeconds(2), deadline.Token);
                }
            }
            catch
            {
                var container = await docker.ContainerIdAsync(instance);
                if (container is not null) await docker.CommandAsync(["stop", "--time", "120", container], TimeSpan.FromSeconds(135));
                store.SetTask(id, "NeedsAttention", "PlayerVerification", "VerificationFailed", message: "启动验证失败，保留恢复点与恢复锁；核实后回退。");
                throw;
            }
            store.SaveInstance(instance with { DesiredPower = "running" });
            store.SetTask(id, "NeedsAttention", "PlayerVerification", "PlayerVerificationPending",
                message: "已显式启动用于核验，restart policy 仍保持 no；待 API 与原玩家核验后再接受。");
        }
        else if (resolution == "force-stop")
        {
            var container = await docker.ContainerIdAsync(instance);
            if (container is not null)
            {
                await docker.CommandAsync(["update", "--restart=no", container], TimeSpan.FromSeconds(10));
                await docker.CommandAsync(["stop", "--time", "0", container], TimeSpan.FromSeconds(15));
            }
            store.SetTask(id, "NeedsAttention", task.Phase, task.SafeCode, message: "管理员显式强制停止；未承诺保存成功，继续回退或核实解锁。");
        }
        else if (resolution == "acknowledge-safe")
        {
            if (task.Kind is "import" or "restore" or "upgrade" || task.Phase is not ("Preflight" or "Save" or "InstallAndStart" or "Start" or "Stop" or "Interrupted"))
                throw new PanelException("RecoveryRequired", "此阶段必须回退或完成验收，不能直接解锁。", 409);
            var container = await docker.ContainerIdAsync(instance);
            if (container is not null && (await docker.InspectAsync(container)).GetProperty("State").GetProperty("Running").GetBoolean())
                throw new PanelException("StopRequired", "核实恢复点并停止实例后才能解锁失败任务。", 409);
            store.SetTask(id, "Failed", "Reviewed", task.SafeCode, message: "管理员核实已停止，解除任务锁；下次操作须重新预检。");
        }
        else if (resolution == "confirm-players")
        {
            if (!playersVerified || task.SafeCode != "PlayerVerificationPending")
                throw new PanelException("PlayerVerificationRequired", "必须由管理员确认真实原玩家核验结果。", 409);
            var info = await game.ReadAsync(instance, "info");
            if (!info.TryGetProperty("worldguid", out var guid) || !string.Equals(guid.GetString(), instance.WorldGuid, StringComparison.OrdinalIgnoreCase))
                throw new PanelException("WorldMismatch", "当前世界与任务预期不一致。", 409);
            if (!info.TryGetProperty("version", out var build) || build.GetString() != instance.GameBuild)
                throw new PanelException("GameBuildMismatch", "当前游戏版本与任务预期不一致。", 409);
            var container = await docker.ContainerIdAsync(instance) ?? throw new PanelException("ContainerNotFound", "实例容器不存在。");
            var env = DockerBackend.EnvironmentValues(await docker.InspectAsync(container));
            if (env.GetValueOrDefault("UPDATE_ON_BOOT") != "false" || env.GetValueOrDefault("AUTO_UPDATE_ENABLED") != "false")
                throw new PanelException("UpdatePolicyUnsafe", "游戏更新开关未禁用，不能解除恢复锁。", 409);
            instances.CheckSource(instance);
            if (!ConfigurationObservation.Matches(await game.ReadAsync(instance, "settings"), instance.Applied ?? instance.Desired))
                throw new PanelException("SettingsMismatch", "实际规则与应用配置不一致。", 409);
            if (task.Kind is "import" or "restore") new SavedSwap(Path.Combine(instance.Root, "data", "Pal"), id).Accept();
            await docker.CommandAsync(["update", "--restart=unless-stopped", container], TimeSpan.FromSeconds(10));
            store.SetTask(id, "Succeeded", "PlayerVerified", message: "管理员确认原玩家实际入服核验；自动检查未代替玩家证据。");
        }
        else if (resolution == "rollback")
        {
            var backup = store.Backups(instance.Id).SingleOrDefault(b => b.Id == task.RecoveryPoint)
                ?? throw new PanelException("RecoveryPointMissing", "任务恢复点不存在，拒绝猜测回退。", 409);
            if (await ZipWorldArchive.HashAsync(Path.Combine(backup.Path, "manifest.json")) != backup.Sha256)
                throw new PanelException("BackupCorrupt", "恢复清单摘要已改变，拒绝回退。", 409);
            var manifest = await BackupService.VerifyAsync(backup.Path);
            if (instance.QuarantinedUtc is not null && task.Kind == "purge")
            {
                var restored = quarantine.Undo(instance);
                store.SetTask(id, "RolledBack", "QuarantineUndone", message: "隔离移动已撤销，保持停止，原最终备份继续受保护。");
                store.Audit(user, restored.Id, "task-rollback", "RolledBack", id);
                instances.InvalidateObservation(instance.Id);
                return store.Task(id);
            }
            var container = await docker.ContainerIdAsync(instance);
            store.SetTask(id, "Running", "RollbackStop", recoveryPoint: backup.Id);
            await ioGate.Semaphore.WaitAsync();
            try
            {
                if (container is not null)
                {
                    var running = (await docker.InspectAsync(container)).GetProperty("State").GetProperty("Running").GetBoolean();
                    if (running) await game.SaveAsync(instance);
                    await docker.CommandAsync(["update", "--restart=no", container], TimeSpan.FromSeconds(10));
                    await docker.CommandAsync(["stop", "--time", "120", container], TimeSpan.FromSeconds(135));
                    if ((await docker.InspectAsync(container)).GetProperty("State").GetProperty("Running").GetBoolean())
                        throw new PanelException("StopUnconfirmed", "无法确认停止，拒绝回退。");
                }
                if (Directory.Exists(Path.Combine(instance.Root, "data", "Pal", "Saved")))
                    await backups.CreateAsync(instance, "pre-rollback", task.Kind == "upgrade");
                if (task.Kind is "import" or "restore")
                    new SavedSwap(Path.Combine(instance.Root, "data", "Pal"), id).Rollback();
                else if (task.Kind == "upgrade")
                {
                    if (!manifest.ContainsInstallation) throw new PanelException("InstallationMissing", "版本回退必须具有配套旧安装。");
                    var swap = new SavedSwap(instance.Root, id + "-rollback", "data");
                    instances.CheckDisk(instance.Root, TreeFiles.Bytes(Path.Combine(backup.Path, "installation")));
                    PrepareReplacement(swap, Path.Combine(backup.Path, "installation"));
                    swap.ResumeCommit(phase => store.SetTask(id, "Running", "Rollback" + phase));
                }
                else
                {
                    var parent = Path.Combine(instance.Root, "data", "Pal");
                    var swap = new SavedSwap(parent, id + "-rollback");
                    var source = manifest.ContainsInstallation ? Path.Combine(backup.Path, "installation", "Pal", "Saved") : Path.Combine(backup.Path, "Saved");
                    PrepareReplacement(swap, source);
                    swap.ResumeCommit(phase => store.SetTask(id, "Running", "Rollback" + phase));
                }
                foreach (var file in new[] { "compose.yaml", "settings.env", "secrets.env" })
                    DurableFile.Write(SafePaths.Within(instance.Root, file), File.ReadAllBytes(SafePaths.Within(backup.Path, file)));
                var restored = manifest.Instance with { DesiredPower = "stopped", Revision = instance.Revision + 1 };
                store.SaveInstance(restored with { SourceHash = instances.SourceHash(restored) });
                // Remove the newer stopped container; next explicit start creates the old image over old installation/Saved.
                if (container is not null) await docker.CommandAsync(["rm", container], TimeSpan.FromSeconds(15));
                store.SetTask(id, "RolledBack", "RollbackComplete", message: "旧版本安装、配置与存档已恢复，保持停止，显式启动后仍需核验。");
            }
            catch
            {
                store.SetTask(id, "NeedsAttention", store.Task(id).Phase, "RollbackInterrupted", recoveryPoint: backup.Id,
                    message: "回退未完成，保留恢复点与现场，禁止自动启动。");
                throw;
            }
            finally { ioGate.Semaphore.Release(); }
        }
        else throw new PanelException("UnsupportedRecovery", "恢复操作无效。", 400);
        store.Audit(user, instance.Id, "task-" + resolution, store.Task(id).State, id);
        instances.InvalidateObservation(instance.Id);
        return store.Task(id);
    }

    private static void PrepareReplacement(SavedSwap swap, string source)
    {
        if (File.Exists(swap.JournalFile)) return;
        SafePaths.RejectLinks(swap.Staging);
        if (Directory.Exists(swap.Staging)) Directory.Delete(swap.Staging, true);
        TreeFiles.Copy(source, swap.Staging);
        TreeFiles.PrepareGameOwnership(swap.Staging);
    }
}
