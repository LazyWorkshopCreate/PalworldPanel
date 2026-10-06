using System.Text;
using System.Text.Json;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Workers;

public sealed class TaskWorker(SqliteStore store, InstanceService instances, DockerBackend docker,
    GameRestClient game, BackupService backups, QuarantineService quarantine, IHostApplicationLifetime lifetime, HeavyIoGate ioGate) : BackgroundService
{
    private readonly SemaphoreSlim heavyIo = ioGate.Semaphore;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StartupGate.WaitAsync(lifetime, stoppingToken);
        foreach (var interrupted in store.Tasks("Running"))
        {
            try
            {
                var instance = store.Instance(interrupted.InstanceId);
                instances.CheckRoot(instance);
                instance = quarantine.ReconcileInterruptedMove(instance, interrupted);
                if (interrupted.Kind != "unmanage")
                {
                await HoldAsync(instance, stoppingToken);
                var container = await docker.ContainerIdAsync(instance, stoppingToken);
                if (container is not null)
                    await docker.CommandAsync(["stop", "--time", "120", container], TimeSpan.FromSeconds(135), cancellation: stoppingToken);
                }
            }
            catch (Exception) { /* Always preserve the durable task as NeedsAttention. */ }
            store.SetTask(interrupted.Id, "NeedsAttention", interrupted.Phase, "ExecutorInterrupted", message: "执行器中断，已禁止自动续跑破坏性阶段。");
        }
        while (!stoppingToken.IsCancellationRequested)
        {
            var completed = running.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray();
            foreach (var id in completed) { await running[id]; running.Remove(id); }
            foreach (var task in store.Tasks("Queued").Where(t => !running.ContainsKey(t.Id)))
                if (running.Count < 4) running.Add(task.Id, RunAsync(task, stoppingToken));
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private readonly Dictionary<string, Task> running = new();

    private async Task RunAsync(TaskRecord task, CancellationToken cancellation)
    {
        InstanceRecord instance;
        try { instance = store.Instance(task.InstanceId); }
        catch (PanelException ex) { store.SetTask(task.Id, "Failed", "Preflight", ex.Code); return; }
        using var payload = JsonDocument.Parse(task.Payload);
        var arguments = payload.RootElement;
        try
        {
            if (task.Kind == "unmanage")
            {
                if (!store.SetTask(task.Id, "Running", "Preflight", expectedState: "Queued")) return;
                store.ForgetInstance(instance.Id);
                store.SetTask(task.Id, "Succeeded", "Complete");
                store.Audit(task.User, task.InstanceId, task.Kind, "Succeeded", task.Id);
                return;
            }
            instances.CheckRoot(instance);
            using var instanceLock = DiskLock.AcquireInstance(instances.Options.StateRoot, instance.Root, instance.Id);
            if (!store.SetTask(task.Id, "Running", "Preflight", expectedState: "Queued")) return;
            if (task.Kind is not ("create" or "unmanage" or "undo-quarantine" or "finalize-purge")) instances.CheckSource(instance);
            if (!instance.Writable && task.Kind is not ("unmanage" or "undo-quarantine" or "finalize-purge" or "adopt")) throw new PanelException("ReadOnlyInstance", "实例处于只读管理模式。", 409);
            switch (task.Kind)
            {
                case "create":
                    instances.CheckDisk(instance.Root, 20L << 30);
                    Phase(task, "WaitForInstallSlot");
                    await heavyIo.WaitAsync(cancellation);
                    try
                    {
                    Phase(task, "PrepareConfiguration");
                    instances.WriteTemplate(instance);
                    await docker.ComposeAsync(instance, ["config", "--quiet"], true, cancellation);
                    instance = instance with { SourceHash = instances.SourceHash(instance) };
                    store.SaveInstance(instance);
                    Phase(task, "InstallAndStart");
                    await StartAsync(instance, cancellation);
                    Phase(task, "ValidateStartup");
                    instance = await ValidateAsync(instance, cancellation);
                    Phase(task, "FinalizeInitialization");
                    await ApproveRestartAsync(instance, cancellation);
                    store.SaveInstance(instance with { Applied = instance.Desired, DesiredPower = "running", SourceHash = instances.SourceHash(instance) });
                    }
                    finally { heavyIo.Release(); }
                    break;
                case "save":
                    Phase(task, "Save"); await game.SaveAsync(instance, cancellation); break;
                case "start":
                    Phase(task, "Start"); await StartAsync(instance, cancellation);
                    instance = await ValidateAsync(instance, cancellation);
                    await ApproveRestartAsync(instance, cancellation);
                    store.SaveInstance(instance with { DesiredPower = "running" }); break;
                case "stop":
                    await StopAsync(instance, task, Bool(arguments, "force"), cancellation);
                    store.SaveInstance(instance with { DesiredPower = "stopped" }); break;
                case "restart":
                case "backup":
                case "apply-config":
                case "upgrade":
                case "import":
                case "restore":
                case "retain-data":
                case "purge":
                case "adopt":
                    await MutateAsync(instance, task, arguments, cancellation); return;
                case "unmanage":
                    store.ForgetInstance(instance.Id); break;
                case "undo-quarantine":
                    quarantine.Undo(instance); break;
                case "finalize-purge":
                    await heavyIo.WaitAsync(cancellation);
                    try { await quarantine.FinalizeAsync(instance, cancellation); }
                    finally { heavyIo.Release(); }
                    break;
                default: throw new PanelException("UnsupportedAction", "该操作尚不受支持。", 400);
            }
            store.SetTask(task.Id, "Succeeded", "Complete");
            store.Audit(task.User, task.InstanceId, task.Kind, "Succeeded", task.Id);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { store.SetTask(task.Id, "NeedsAttention", "Interrupted", "ExecutorInterrupted"); }
        catch (Exception error)
        {
            var code = error is PanelException known ? known.Code : "TaskFailed";
            if (store.Task(task.Id).Phase is not ("Preflight" or "Save"))
            {
                try
                {
                    instance = store.Instance(task.InstanceId);
                    await HoldAsync(instance, CancellationToken.None);
                    var container = await docker.ContainerIdAsync(instance);
                    if (container is not null) await docker.CommandAsync(["stop", "--time", "120", container], TimeSpan.FromSeconds(135));
                }
                catch (Exception) { }
            }
            var phase = store.Task(task.Id).Phase;
            store.SetTask(task.Id, phase is "Preflight" or "Save" ? "Failed" : "NeedsAttention", phase, code,
                message: "操作未完成；核实实际状态后处理，不会自动重跑。");
            store.Audit(task.User, task.InstanceId, task.Kind, code, task.Id);
        }
        finally { instances.InvalidateObservation(task.InstanceId); }
    }

    private async Task MutateAsync(InstanceRecord instance, TaskRecord task, JsonElement payload, CancellationToken cancellation)
    {
        if (task.Kind == "purge" && !instance.Owned) throw new PanelException("LegacyPurgeDisabled", "既有实例禁用面板永久清理。", 409);
        if (task.Kind == "adopt")
        {
            if (instance.Writable || !Bool(payload, "externalSchedulesDisabled"))
                throw new PanelException("ExternalScheduleUnconfirmed", "必须先核实外部备份/更新/重启任务已停用，禁止重复接管。", 409);
            KnownCompose.Read(instance, instances.Options);
        }
        if (task.Kind == "upgrade" && (!instance.Desired.HasSameValues(instance.Applied) ||
            (instance.AppliedAdminCipher is not null && instance.AppliedAdminCipher != instance.AdminCipher) ||
            (instance.AppliedGameCipher is not null && instance.AppliedGameCipher != instance.GameCipher)))
            throw new PanelException("DraftPending", "请先处理配置草稿，再执行升级。", 409);
        if (task.Kind == "upgrade")
        {
            var image = payload.TryGetProperty("image", out var requestedImage) ? requestedImage.GetString() : null;
            var build = payload.TryGetProperty("expectedGameBuild", out var requestedBuild) ? requestedBuild.GetString() : null;
            var mode = payload.TryGetProperty("mode", out var requestedMode) ? requestedMode.GetString() : null;
            if (mode is not ("image" or "game" or "both") || image is null || !instances.Options.AllowedImages.Contains(image, StringComparer.Ordinal) ||
                !image.Contains("@sha256:", StringComparison.Ordinal) || string.IsNullOrEmpty(build) || build.Length > 128 || build.Any(char.IsControl) ||
                (mode == "game" && image != instance.Image) || (mode == "image" && build != instance.GameBuild))
                throw new PanelException("UpgradeNotApproved", "须选择 image/game/both、批准的 digest 和预期游戏 build。", 400);
            await docker.CommandAsync(["pull", image], TimeSpan.FromMinutes(15), cancellation: cancellation);
        }
        var id = await docker.ContainerIdAsync(instance, cancellation);
        var wasRunning = id is not null && (await docker.InspectAsync(id, cancellation)).GetProperty("State").GetProperty("Running").GetBoolean();
        var savedBytes = TreeFiles.Bytes(SafePaths.Within(instance.Root, "data/Pal/Saved"));
        var backupBytes = task.Kind == "upgrade" ? TreeFiles.Bytes(SafePaths.Within(instance.Root, "data")) : savedBytes;
        var stagingBytes = task.Kind == "upgrade" ? Math.Max(backupBytes, 20L << 30) : 0;
        if (task.Kind == "import")
        {
            var uploadId = payload.GetProperty("uploadId").GetString()!;
            if (!Guid.TryParseExact(uploadId, "N", out _)) throw new PanelException("InvalidUpload", "上传标识无效。", 400);
            var plan = await ZipWorldArchive.InspectAsync(SafePaths.Within(instances.Options.StateRoot, $"uploads/{instance.Id}/{uploadId}.zip"), new(), cancellation);
            if (plan.Sha256 != payload.GetProperty("sha256").GetString() || !plan.Worlds.Contains(payload.GetProperty("world").GetString(), StringComparer.Ordinal))
                throw new PanelException("UploadChanged", "上传包或所选世界不匹配。", 409);
            stagingBytes = checked(savedBytes + plan.ExpandedBytes);
        }
        if (task.Kind == "restore")
        {
            var point = store.Backups(instance.Id).SingleOrDefault(backup => backup.Id == payload.GetProperty("backupId").GetString())
                ?? throw new PanelException("BackupNotFound", "恢复点不属于当前实例。", 404);
            if (await ZipWorldArchive.HashAsync(Path.Combine(point.Path, "manifest.json"), cancellation) != point.Sha256)
                throw new PanelException("BackupCorrupt", "恢复清单摘要已改变。", 409);
            var manifest = await BackupService.VerifyAsync(point.Path, cancellation);
            if (manifest.Instance.Id != instance.Id || manifest.Instance.GameBuild != instance.GameBuild)
                throw new PanelException("VersionMismatch", "普通恢复不能跨游戏版本或实例。", 409);
            stagingBytes = checked(savedBytes + point.Bytes);
        }
        // Conservatively include both allocations even if roots share one filesystem.
        instances.CheckDisk(instances.Options.BackupRoot, checked(backupBytes + stagingBytes));
        instances.CheckDisk(instance.Root, checked(backupBytes + stagingBytes));
        await heavyIo.WaitAsync(cancellation);
        try
        {
            await StopAsync(instance, task, false, cancellation);
            Phase(task, "OfflineBackup");
            var backup = await backups.CreateAsync(instance, task.Kind is "backup" ? task.User == "scheduler" ? "scheduled" : "manual" : task.Kind,
                task.Kind == "upgrade", cancellation);
            store.SetTask(task.Id, "Running", "RecoveryPointVerified", recoveryPoint: backup.Id);
            if (task.Kind == "purge")
            {
                Phase(task, "Quarantine");
                await docker.ComposeAsync(instance, ["rm", "--force", "--stop", instance.Service], true, cancellation);
                var parent = Path.GetDirectoryName(instance.Root)!;
                var quarantineParent = SafePaths.Within(parent, ".quarantine");
                Directory.CreateDirectory(quarantineParent);
                var destination = SafePaths.Within(quarantineParent, instance.Id);
                var quarantineUtc = DateTimeOffset.UtcNow;
                DurableFile.WriteJson(Path.Combine(instance.Root, "quarantine.json"), new QuarantineRecord(instance.Id, instance.Root, backup.Id, quarantineUtc));
                Directory.Move(instance.Root, destination);
                DurableFile.FlushDirectory(parent);
                store.SaveInstance(instance with { Root = destination, Writable = false, DesiredPower = "stopped",
                    ContainerId = null, QuarantinedUtc = quarantineUtc.ToString("O") });
                store.SetTask(task.Id, "Succeeded", "Quarantined", recoveryPoint: backup.Id,
                    message: "目录隔离保留至少 7 天，最终独立备份受保护，不随目录清理。");
                store.Audit(task.User, instance.Id, "purge", "Quarantined", task.Id);
                return;
            }
            if (task.Kind is "import" or "restore")
            {
                instance = await InstallWorldAsync(instance, task, payload, cancellation);
                if (wasRunning)
                {
                    await StartAsync(instance, cancellation);
                    instance = await ValidateAsync(instance, cancellation);
                }
                store.SaveInstance(instance with { SourceHash = instances.SourceHash(instance), DesiredPower = wasRunning ? "running" : "stopped" });
                store.SetTask(task.Id, "NeedsAttention", "PlayerVerification", "PlayerVerificationPending",
                    message: wasRunning ? "自动检查通过，等待原玩家真实入服核验。" : "原实例保持停止；显式启动验证后再确认原玩家。");
                return;
            }
            if (task.Kind is "apply-config" or "adopt")
            {
                Phase(task, "ApplyConfig");
                if (task.Kind == "adopt") { instance = instance with { Writable = true }; store.SaveInstance(instance); }
                instances.WriteTemplate(instance);
                await docker.ComposeAsync(instance, ["config", "--quiet"], true, cancellation);
                instance = instance with { Applied = instance.Desired, AppliedAdminCipher = instance.AdminCipher, AppliedGameCipher = instance.GameCipher };
            }
            if (task.Kind == "upgrade")
            {
                var image = payload.GetProperty("image").GetString()!;
                var expectedBuild = payload.GetProperty("expectedGameBuild").GetString()!;
                if (!instances.Options.AllowedImages.Contains(image, StringComparer.Ordinal) || !image.Contains("@sha256:", StringComparison.Ordinal) ||
                    expectedBuild.Length is < 1 or > 128 || expectedBuild.Any(char.IsControl))
                    throw new PanelException("UpgradeNotApproved", "镜像须使用批准的 digest，且必须指定预期游戏版本。", 409);
                instance = instance with { Image = image };
                instances.WriteTemplate(instance);
                store.SaveInstance(instance with { SourceHash = instances.SourceHash(instance) });
                Phase(task, "UpgradeInstallation");
                await docker.ComposeAsync(instance, ["up", "--detach", "--no-deps", "--force-recreate", "--pull", "never", instance.Service], true,
                    cancellation, updateInstallation: payload.GetProperty("mode").GetString() is "game" or "both");
                instance = await ValidateAsync(instance, cancellation);
                if (instance.GameBuild != expectedBuild) throw new PanelException("GameBuildMismatch", "实际游戏 build 不等于预期版本，禁止自动接受。", 409);
                // Recreate with updates disabled before any restart policy can be approved.
                await docker.ComposeAsync(instance, ["up", "--detach", "--no-deps", "--force-recreate", "--pull", "never", instance.Service], true, cancellation);
                instance = await ValidateAsync(instance, cancellation);
                if (instance.GameBuild != expectedBuild) throw new PanelException("GameBuildMismatch", "固定升级结果校验失败，保持恢复锁。", 409);
                if (!wasRunning) await StopAsync(instance, task, false, cancellation);
                store.SaveInstance(instance with { SourceHash = instances.SourceHash(instance), DesiredPower = wasRunning ? "running" : "stopped" });
                store.SetTask(task.Id, "NeedsAttention", "PlayerVerification", "PlayerVerificationPending",
                    message: "镜像和游戏版本核验通过，保留旧安装及旧存档恢复点，等待原玩家核验。");
                return;
            }
            if (task.Kind == "retain-data")
            {
                Phase(task, "RemoveContainerRetainData");
                await docker.ComposeAsync(instance, ["rm", "--force", "--stop", instance.Service], true, cancellation);
                store.SaveInstance(instance with { DesiredPower = "stopped", ContainerId = null });
            }
            else if (wasRunning || task.Kind == "restart")
            {
                await StartAsync(instance, cancellation);
                instance = await ValidateAsync(instance, cancellation);
                await ApproveRestartAsync(instance, cancellation);
                store.SaveInstance(instance with { Applied = task.Kind is "apply-config" or "adopt" ? instance.Desired : instance.Applied,
                    DesiredPower = "running", SourceHash = instances.SourceHash(instance) });
            }
            else store.SaveInstance(instance with { Applied = task.Kind is "apply-config" or "adopt" ? instance.Desired : instance.Applied, SourceHash = instances.SourceHash(instance) });
            store.SetTask(task.Id, "Succeeded", "Complete");
            if (task.Kind == "backup") backups.Prune(instance);
            store.Audit(task.User, task.InstanceId, task.Kind, "Succeeded", task.Id);
        }
        finally { heavyIo.Release(); }
    }

    private async Task<InstanceRecord> InstallWorldAsync(InstanceRecord instance, TaskRecord task, JsonElement payload, CancellationToken cancellation)
    {
        var parent = SafePaths.Within(instance.Root, "data/Pal");
        var swap = new SavedSwap(parent, task.Id);
        var saved = SafePaths.Within(parent, "Saved");
        Phase(task, "StageWorld");
        TreeFiles.Copy(saved, swap.Staging);
        var games = SafePaths.Within(swap.Staging, "SaveGames");
        if (Directory.Exists(games)) Directory.Delete(games, true);
        string world;
        if (task.Kind == "import")
        {
            var uploadId = payload.GetProperty("uploadId").GetString()!;
            if (!Guid.TryParseExact(uploadId, "N", out _)) throw new PanelException("InvalidUpload", "上传标识无效。");
            var upload = SafePaths.Within(instances.Options.StateRoot, $"uploads/{instance.Id}/{uploadId}.zip");
            var plan = await ZipWorldArchive.InspectAsync(upload, new(), cancellation);
            if (plan.Sha256 != payload.GetProperty("sha256").GetString()) throw new PanelException("UploadChanged", "上传包已变化。", 409);
            world = payload.GetProperty("world").GetString()!;
            await ZipWorldArchive.ExtractWorldAsync(upload, plan, world, Path.Combine(games, "0", world.ToUpperInvariant()), new(), cancellation);
            world = world.ToUpperInvariant();
        }
        else
        {
            var backupId = payload.GetProperty("backupId").GetString();
            var backup = store.Backups(instance.Id).FirstOrDefault(b => b.Id == backupId)
                ?? throw new PanelException("BackupNotFound", "恢复点不属于当前实例。", 404);
            if (await ZipWorldArchive.HashAsync(Path.Combine(backup.Path, "manifest.json"), cancellation) != backup.Sha256)
                throw new PanelException("BackupCorrupt", "恢复清单摘要已改变，拒绝恢复。", 409);
            var manifest = await BackupService.VerifyAsync(backup.Path, cancellation);
            if (manifest.Instance.Id != instance.Id || manifest.Instance.GameBuild != instance.GameBuild)
                throw new PanelException("VersionMismatch", "普通恢复不能跨游戏版本或实例。", 409);
            world = backup.WorldGuid ?? throw new PanelException("UnknownWorld", "恢复点世界未知。");
            var restore = manifest.ContainsInstallation ? Path.Combine(backup.Path, "installation", "Pal", "Saved") : Path.Combine(backup.Path, "Saved");
            Directory.Delete(swap.Staging, true);
            TreeFiles.Copy(restore, swap.Staging);
            var targetConfig = SafePaths.Within(swap.Staging, "Config");
            if (Directory.Exists(targetConfig)) Directory.Delete(targetConfig, true);
            TreeFiles.Copy(SafePaths.Within(saved, "Config"), targetConfig);
            var restoredWorld = WorldFiles.Find(swap.Staging, world);
            var normalizedWorld = SafePaths.Within(swap.Staging, "SaveGames/0/" + world);
            if (restoredWorld != normalizedWorld) Directory.Move(restoredWorld, normalizedWorld);
        }
        var config = SafePaths.Within(swap.Staging, "Config/LinuxServer/GameUserSettings.ini");
        if (!File.Exists(config)) throw new PanelException("UnsupportedConfig", "世界选择配置不存在，不能猜测生成。");
        var source = File.ReadAllText(config);
        var regex = new System.Text.RegularExpressions.Regex("(?m)^DedicatedServerName=.*$");
        if (regex.Matches(source).Count != 1) throw new PanelException("UnsupportedConfig", "世界选择配置不唯一。");
        DurableFile.Write(config, Encoding.UTF8.GetBytes(regex.Replace(source, "DedicatedServerName=" + world)));
        TreeFiles.PrepareGameOwnership(swap.Staging);
        Phase(task, "SwapPrepared");
        swap.Commit(phase => Phase(task, phase));
        var result = instance with { WorldGuid = world };
        store.SaveInstance(result);
        return result;
    }

    public async Task HoldAsync(InstanceRecord instance, CancellationToken cancellation)
    {
        var id = await docker.ContainerIdAsync(instance, cancellation);
        if (id is not null) await docker.CommandAsync(["update", "--restart=no", id], TimeSpan.FromSeconds(10), cancellation: cancellation);
    }

    private async Task StopAsync(InstanceRecord instance, TaskRecord task, bool force, CancellationToken cancellation)
    {
        var id = await docker.ContainerIdAsync(instance, cancellation);
        if (id is null) return;
        var running = (await docker.InspectAsync(id, cancellation)).GetProperty("State").GetProperty("Running").GetBoolean();
        if (running && !force)
        {
            Phase(task, "Save");
            await game.SaveAsync(instance, cancellation);
        }
        Phase(task, "RestartHold");
        DurableFile.WriteJson(Path.Combine(instance.Root, "power-hold.json"), new { taskId = task.Id, wasRunning = running, desiredRestart = "unless-stopped" });
        await HoldAsync(instance, cancellation);
        Phase(task, "Stop");
        await docker.CommandAsync(["stop", "--time", force ? "0" : "120", id], TimeSpan.FromSeconds(135), cancellation: cancellation);
        if ((await docker.InspectAsync(id, cancellation)).GetProperty("State").GetProperty("Running").GetBoolean())
            throw new PanelException("StopUnconfirmed", "无法确认容器停止。", 409);
    }

    private Task StartAsync(InstanceRecord instance, CancellationToken cancellation) => docker.ComposeAsync(instance,
        ["up", "--detach", "--no-deps", "--pull", "never", instance.Service], true, cancellation);

    private async Task<InstanceRecord> ValidateAsync(InstanceRecord instance, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(TimeSpan.FromMinutes(30));
        while (!limit.IsCancellationRequested)
        {
            try
            {
                var info = await game.ReadAsync(instance, "info", limit.Token);
                var world = info.TryGetProperty("worldguid", out var guid) ? guid.GetString() : null;
                if (world is null || !System.Text.RegularExpressions.Regex.IsMatch(world, "^[a-fA-F0-9]{32}$"))
                    throw new PanelException("UnknownWorld", "运行世界标识无效。", 409);
                if (instance.WorldGuid is not null && !string.Equals(instance.WorldGuid, world, StringComparison.OrdinalIgnoreCase))
                    throw new PanelException("WorldMismatch", "运行世界与预期不一致，保持停止。", 409);
                var settings = await game.ReadAsync(instance, "settings", limit.Token);
                if ((instance.Applied ?? instance.Desired).Additional is { Count: > 0 } extra)
                {
                    var configured = GameSettingsFile.Read(instance);
                    if (extra.Any(p => !configured.TryGetValue(p.Key, out var actual) ||
                        GameSettingCatalog.Encode(GameSettingCatalog.Definitions.Single(d => d.Key == p.Key), actual) !=
                        GameSettingCatalog.Encode(GameSettingCatalog.Definitions.Single(d => d.Key == p.Key), p.Value)))
                        throw new PanelException("SettingsMismatch", "扩展参数文件与已应用配置不一致。", 409);
                }
                if (!info.TryGetProperty("version", out var actualBuild) || actualBuild.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(actualBuild.GetString()))
                    throw new PanelException("UnknownGameBuild", "无法核实实际游戏版本，保持恢复锁。", 409);
                await game.ReadAsync(instance, "metrics", limit.Token);
                if (!ConfigurationObservation.Matches(settings, instance.Applied ?? instance.Desired))
                    throw new PanelException("SettingsMismatch", "实际游戏规则与已应用配置不一致，保持恢复锁。", 409);
                var containerId = await docker.ContainerIdAsync(instance, limit.Token)
                    ?? throw new PanelException("ContainerNotFound", "实例容器身份无法确认。");
                var actual = (await docker.InspectAsync(containerId, limit.Token)).GetProperty("HostConfig");
                var expected = instance.Applied ?? instance.Desired;
                if (actual.GetProperty("Memory").GetInt64() != expected.MemoryMiB * 1024 * 1024 ||
                    actual.GetProperty("NanoCpus").GetInt64() != (long)(expected.Cpu * 1_000_000_000))
                    throw new PanelException("ResourceLimitMismatch", "实际容器资源限制与已应用配置不一致。", 409);
                return instance with { WorldGuid = world, GameBuild = info.TryGetProperty("version", out var version) ? version.GetString() : null,
                    ContainerId = await docker.ContainerIdAsync(instance, limit.Token) };
            }
            catch (HttpRequestException) { }
            catch (PanelException ex) when (ex.Code == "GameApiUnavailable") { }
            catch (TaskCanceledException) when (!limit.IsCancellationRequested) { }
            await Task.Delay(TimeSpan.FromSeconds(2), limit.Token);
        }
        throw new PanelException("StartupTimeout", "游戏未在期限内完成启动，保持恢复锁。", 409);
    }

    private async Task ApproveRestartAsync(InstanceRecord instance, CancellationToken cancellation)
    {
        var id = await docker.ContainerIdAsync(instance, cancellation) ?? throw new PanelException("ContainerNotFound", "容器不存在。", 409);
        await docker.CommandAsync(["update", "--restart=unless-stopped", id], TimeSpan.FromSeconds(10), cancellation: cancellation);
    }
    private void Phase(TaskRecord task, string phase) => store.SetTask(task.Id, "Running", phase);
    private static bool Bool(JsonElement payload, string name) => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
