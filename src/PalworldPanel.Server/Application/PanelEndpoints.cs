using System.Security.Claims;
using System.Text.Json;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed record CreateRequest(GameRules Rules, string? CloneId = null, CreationPlan? Plan = null,
    string? Confirmation = null, string? PreviewHash = null);
public sealed record ActionRequest(string Kind, string? Confirmation = null, string? PreviewHash = null,
    JsonElement? Arguments = null, string? TypedName = null);
public sealed record RecoveryRequest(string Resolution, bool OriginalPlayersVerified = false,
    string? Confirmation = null, string? PreviewHash = null);
public sealed record ExportRequest(string Passphrase);
public sealed record SecretsRequest(string? AdministratorPassword = null, string? GamePassword = null);
public sealed record BackupPolicyRequest(string Time, int RetentionDays);

public static class PanelEndpoints
{
    private static readonly HashSet<string> Actions = new(StringComparer.Ordinal)
        { "start", "stop", "restart", "save", "backup", "apply-config", "import", "restore", "upgrade", "retain-data", "purge", "unmanage", "undo-quarantine", "finalize-purge", "adopt" };

    public static void MapPanelEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").RequireAuthorization();
        api.MapGet("/capabilities", (PanelOptions options) => Results.Ok(new { approvedImages = options.AllowedImages,
            options.DesktopValidation, archiveUploadGiB = 4, expandedGiB = 20, maxFiles = 100000,
            versionRollbackRequiresInstallation = true, arbitraryComposeWrite = false }));
        api.MapGet("/discovery", async (DiscoveryService discovery, CancellationToken ct) => Results.Ok(await discovery.DiscoverAsync(ct)));
        api.MapPost("/discovery/register", async (RegisterRequest request, HttpContext context, DiscoveryService discovery, CancellationToken ct) =>
        {
            AdministratorSecurity.RequireRecent(context.User);
            return Results.Ok(InstanceService.View(await discovery.RegisterAsync(request.ContainerId, User(context), ct)));
        });
        api.MapGet("/host", async (InstanceService service, CancellationToken ct) => Results.Ok(await service.HostAsync(ct)));
        api.MapPost("/panel-backups/export", async (ExportRequest request, HttpContext context, PanelBackupService backups, SqliteStore store, CancellationToken ct) =>
        {
            AdministratorSecurity.RequireRecent(context.User);
            var stream = await backups.ExportAsync(request.Passphrase, ct);
            store.Audit(User(context), null, "panel-encrypted-export", "Succeeded");
            return Results.File(stream, "application/octet-stream", "palworldpanel-disaster.ppbak");
        });
        api.MapGet("/downloads/{id}", (string id, HttpContext context, PreparedDownloads downloads) =>
            downloads.Consume(id, User(context), context.Connection.RemoteIpAddress?.ToString() ?? "unknown"));
        api.MapPost("/panel-backups/export-prepare", async (ExportRequest request, HttpContext context,
            PanelBackupService backups, PreparedDownloads downloads, SqliteStore store, CancellationToken ct) =>
        {
            AdministratorSecurity.RequireRecent(context.User);
            var stream = await backups.ExportAsync(request.Passphrase, ct);
            store.Audit(User(context), null, "panel-encrypted-export", "Prepared");
            return Results.Ok(downloads.Prepare(stream, User(context), context.Connection.RemoteIpAddress?.ToString() ?? "unknown", "palworldpanel-disaster.ppbak"));
        });
        api.MapGet("/instances", async (SqliteStore store, InstanceService service, CancellationToken ct) =>
            Results.Ok(await Task.WhenAll(store.Instances().Select(instance => service.AddressViewAsync(instance, ct)))));
        api.MapGet("/audit", (SqliteStore store) => Results.Ok(store.AuditEvents()));
        api.MapGet("/backups", (SqliteStore store) => Results.Ok(store.AllBackups().Select(b => new
        { b.Id, b.InstanceId, b.CreatedUtc, b.Bytes, b.WorldGuid, b.GameBuild, b.Protected, b.Purpose, b.ContainsInstallation })));
        api.MapPost("/creation-previews", async (CreateRequest request, HttpContext context, InstanceService service, ConfirmationTokens tokens, CancellationToken ct) =>
        {
            AdministratorSecurity.RequireRecent(context.User);
            if (request.Rules is null) throw new PanelException("InvalidSetting", "新建规则不能为空。", 400);
            var plan = await service.PreviewCreationAsync(request.Rules, request.CloneId, ct);
            var hash = InstanceService.CreationHash(request.Rules, request.CloneId, plan);
            var confirmation = tokens.Issue(User(context), plan.Id, "create", 0, hash);
            return Results.Ok(new { plan, token = confirmation.Token, hash });
        });
        api.MapPost("/instances", async (CreateRequest request, HttpContext context, InstanceService service, ConfirmationTokens tokens, CancellationToken ct) =>
        {
            AdministratorSecurity.RequireRecent(context.User);
            if (request.Rules is null) throw new PanelException("InvalidSetting", "新建规则不能为空。", 400);
            var plan = request.Plan ?? throw new PanelException("PreviewRequired", "请先预检新建目录、端口与预算。", 409);
            var hash = InstanceService.CreationHash(request.Rules, request.CloneId, plan);
            if (request.PreviewHash != hash) throw new PanelException("PreviewChanged", "新建内容已变化，请重新预检。", 409);
            var existing = service.Store.FindRequest(User(context), Key(context));
            if (existing is not null)
            {
                if (existing.Kind != "create" || existing.RequestHash != hash)
                    throw new PanelException("IdempotencyConflict", "重复请求内容不同。", 409);
                return Results.Accepted(value: TaskView(existing));
            }
            tokens.Consume(request.Confirmation ?? "", User(context), plan.Id, "create", 0, hash);
            return Results.Accepted(value: TaskView(await service.CreateAsync(request.Rules, request.CloneId, plan,
                User(context), Key(context), ct)));
        });
        api.MapGet("/instances/{id}", (string id, HttpContext context, SqliteStore store) =>
        {
            var instance = store.Instance(id);
            context.Response.Headers.ETag = $"\"{instance.Revision}\"";
            return Results.Ok(InstanceService.View(instance));
        });
        api.MapGet("/instances/{id}/observations", async (string id, InstanceService service, CancellationToken ct) =>
            Results.Ok(await service.ObserveAsync(service.Store.Instance(id), ct)));
        api.MapGet("/instances/{id}/settings", async (string id, SqliteStore store, GameRestClient game, SecretVault vault, HttpContext context, CancellationToken ct) =>
        {
            var instance = store.Instance(id);
            context.Response.Headers.ETag = $"\"{instance.Revision}\"";
            Dictionary<string, object?>? observed = null;
            var status = "unknown";
            try { observed = ConfigurationObservation.Project(await game.ReadAsync(instance, "settings", ct)); status = "observed"; }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException or PanelException) { }
            Dictionary<string, string>? configured = null;
            if (instance.SourceHash is not null) { try { configured = GameSettingsFile.Read(instance); } catch (PanelException) { } }
            return Results.Ok(new { instance.Desired, instance.Applied, observed, configured, status, updatedUtc = DateTimeOffset.UtcNow,
                gamePasswordConfigured = SecretVault.IsConfigured(instance.GameCipher), administratorConfigured = SecretVault.IsConfigured(instance.AdminCipher),
                passwordStates = PasswordStates.Project(instance, vault) });
        });
        api.MapPatch("/instances/{id}/secrets", (string id, SecretsRequest request, HttpContext context,
            SqliteStore store, InstanceService service, SecretVault vault) =>
        {
            AdministratorSecurity.RequireRecent(context.User);
            var instance = store.Instance(id);
            service.CheckRoot(instance);
            using var instanceLock = DiskLock.AcquireInstance(service.Options.StateRoot, instance.Root, instance.Id);
            instance = store.Instance(id);
            Match(context, instance);
            if (!instance.Writable) throw new PanelException("ReadOnlyInstance", "实例不能写入配置。", 409);
            service.CheckSource(instance);
            if (store.Tasks("Queued").Concat(store.Tasks("Running")).Concat(store.Tasks("NeedsAttention")).Any(t => t.InstanceId == id))
                throw new PanelException("TaskConflict", "实例有未结束任务。", 409);
            if (request.AdministratorPassword is null && request.GamePassword is null)
                throw new PanelException("EmptySecretChange", "未提供密码变更。", 400);
            if ((request.AdministratorPassword is not null && (request.AdministratorPassword.Length is < 16 or > 128 || request.AdministratorPassword.Any(char.IsControl))) ||
                (request.GamePassword is not null && (request.GamePassword.Length > 128 || request.GamePassword.Any(char.IsControl))))
                throw new PanelException("InvalidSecret", "密码长度或字符不符合要求。", 400);
            var updated = instance with {
                AppliedAdminCipher = instance.AppliedAdminCipher ?? instance.AdminCipher,
                AppliedGameCipher = instance.AppliedGameCipher ?? instance.GameCipher,
                AdminCipher = request.AdministratorPassword is null ? instance.AdminCipher : vault.Seal(request.AdministratorPassword, id + ":admin"),
                GameCipher = request.GamePassword is null ? instance.GameCipher : vault.Seal(request.GamePassword, id + ":game"), Revision = instance.Revision + 1 };
            store.SaveInstance(updated);
            store.Audit(User(context), id, "secrets-draft", "Succeeded");
            return Results.Ok(InstanceService.View(updated));
        });
        api.MapPatch("/instances/{id}/backup-policy", (string id, BackupPolicyRequest request, HttpContext context, SqliteStore store, InstanceService service) =>
        {
            var instance = store.Instance(id);
            service.CheckRoot(instance);
            using var instanceLock = DiskLock.AcquireInstance(service.Options.StateRoot, instance.Root, instance.Id);
            instance = store.Instance(id);
            Match(context, instance);
            if (!instance.Writable) throw new PanelException("ReadOnlyInstance", "只读实例不启用调度。", 409);
            if (request.RetentionDays is < 1 or > 365 || !TimeOnly.TryParseExact(request.Time, "HH:mm",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                throw new PanelException("InvalidBackupPolicy", "备份时间须 HH:mm，保留期为 1 至 365 天。", 400);
            store.SaveInstance(instance with { BackupTime = request.Time, RetentionDays = request.RetentionDays, Revision = instance.Revision + 1 });
            store.Audit(User(context), id, "backup-policy", "Succeeded");
            return Results.Ok(InstanceService.View(store.Instance(id)));
        });
        api.MapGet("/instances/{id}/backups", (string id, SqliteStore store) =>
        {
            store.Instance(id);
            return Results.Ok(store.Backups(id).Select(b => new { b.Id, b.CreatedUtc, b.Bytes, b.WorldGuid,
                b.GameBuild, b.Image, b.Protected, b.ContainsInstallation, b.Purpose, b.Sha256 }));
        });
        api.MapPost("/instances/{id}/backups/{backupId}/export", async (string id, string backupId,
            ExportRequest request, HttpContext context, BackupExportService exports, SqliteStore store, CancellationToken ct) =>
        {
            AdministratorSecurity.RequireRecent(context.User);
            var stream = await exports.ExportAsync(id, backupId, request.Passphrase, ct);
            store.Audit(User(context), id, "backup-encrypted-export", "Succeeded");
            return Results.File(stream, "application/octet-stream", "palworldpanel-" + backupId + ".ppbak");
        });
        api.MapPost("/instances/{id}/backups/{backupId}/export-prepare", async (string id, string backupId,
            ExportRequest request, HttpContext context, BackupExportService exports, PreparedDownloads downloads, SqliteStore store, CancellationToken ct) =>
        {
            AdministratorSecurity.RequireRecent(context.User);
            var stream = await exports.ExportAsync(id, backupId, request.Passphrase, ct);
            store.Audit(User(context), id, "backup-encrypted-export", "Prepared");
            return Results.Ok(downloads.Prepare(stream, User(context), context.Connection.RemoteIpAddress?.ToString() ?? "unknown", "palworldpanel-" + backupId + ".ppbak"));
        });
        api.MapGet("/instances/{id}/logs", async (string id, InstanceService service, DockerBackend docker,
            SecretVault vault, CancellationToken ct) =>
        {
            var instance = service.Store.Instance(id);
            var container = await docker.ContainerIdAsync(instance, ct);
            if (container is null) return Results.Ok(new { text = "容器不存在。", updatedUtc = DateTimeOffset.UtcNow });
            var result = await docker.RunAsync(["logs", "--tail", "200", "--timestamps", container], TimeSpan.FromSeconds(10), cancellation: ct, captureLogErrors: true);
            var text = LogRedactor.Clean(LogRedactor.LatestFirst(result.Output), vault.Open(instance.AdminCipher, id + ":admin"), vault.Open(instance.GameCipher, id + ":game"),
                vault.Open(instance.AppliedAdminCipher ?? instance.AdminCipher, id + ":admin"), vault.Open(instance.AppliedGameCipher ?? instance.GameCipher, id + ":game"));
            return Results.Ok(new { text, updatedUtc = DateTimeOffset.UtcNow });
        });
        api.MapPatch("/instances/{id}/settings", async (string id, GameRules rules, HttpContext context,
            InstanceService service, CancellationToken ct) =>
        {
            rules.Validate();
            var instance = service.Store.Instance(id);
            rules = rules with { Additional = rules.Additional ?? instance.Desired.Additional };
            rules.Validate();
            service.CheckRoot(instance);
            using var instanceLock = DiskLock.AcquireInstance(service.Options.StateRoot, instance.Root, instance.Id);
            using var budgetLock = DiskLock.Acquire(Path.Combine(service.Options.StateRoot, "allocation.lock"));
            instance = service.Store.Instance(id);
            Match(context, instance);
            if (!instance.Writable) throw new PanelException("ReadOnlyInstance", "实例尚未完成写入接管。", 409);
            service.CheckSource(instance);
            if (service.Store.Tasks("Queued").Concat(service.Store.Tasks("Running")).Concat(service.Store.Tasks("NeedsAttention")).Any(t => t.InstanceId == id))
                throw new PanelException("TaskConflict", "实例有未结束任务。", 409);
            await service.ValidateBudgetAsync(instance, rules, ct);
            var updated = instance with { Name = rules.Name, Desired = rules, Revision = instance.Revision + 1 };
            service.Store.SaveInstance(updated);
            service.Store.Audit(User(context), id, "settings-draft", "Succeeded");
            return Results.Ok(InstanceService.View(updated));
        });
        api.MapPost("/instances/{id}/previews", (string id, ActionRequest request, HttpContext context,
            InstanceService service, ConfirmationTokens tokens) =>
        {
            CheckAction(request.Kind);
            var instance = service.Store.Instance(id);
            if (request.Kind is not ("unmanage" or "undo-quarantine" or "finalize-purge")) service.CheckSource(instance);
            var hash = PreviewHash(instance, request);
            var preview = tokens.Issue(User(context), id, request.Kind, instance.Revision, hash);
            return Results.Ok(new { preview.Token, hash, instance.Revision, instance.WorldGuid,
                root = instance.Root, backups = Path.Combine(service.Options.BackupRoot, instance.Id),
                instance.GameBuild, expiresUtc = preview.ExpiresUtc, stopsInstance = request.Kind is not ("start" or "save" or "unmanage") });
        });
        api.MapPost("/instances/{id}/actions", (string id, ActionRequest request, HttpContext context,
            InstanceService service, ConfirmationTokens tokens) =>
        {
            CheckAction(request.Kind);
            AdministratorSecurity.RequireRecent(context.User);
            var instance = service.Store.Instance(id);
            var payload = request.Arguments?.GetRawText() ?? "{}";
            var hash = PreviewHash(instance, request);
            var key = Key(context);
            var existing = service.Store.FindRequest(User(context), key);
            if (existing is not null)
            {
                if (existing.RequestHash != request.PreviewHash || existing.InstanceId != id || existing.Kind != request.Kind || existing.Payload != payload)
                    throw new PanelException("IdempotencyConflict", "重复请求内容不同。", 409);
                return Results.Accepted(value: TaskView(existing));
            }
            if (request.Kind is "purge" or "finalize-purge" && request.TypedName != instance.Name)
                throw new PanelException("NameConfirmationRequired", "清理前必须输入当前实例的完整显示名。", 400);
            if (request.Kind != "unmanage") service.CheckRoot(instance);
            using var instanceLock = request.Kind == "unmanage" ? null : DiskLock.AcquireInstance(service.Options.StateRoot, instance.Root, instance.Id);
            instance = service.Store.Instance(id);
            hash = PreviewHash(instance, request);
            Match(context, instance);
            tokens.Consume(request.Confirmation ?? "", User(context), id, request.Kind, instance.Revision, hash);
            if (request.PreviewHash != hash) throw new PanelException("PreviewChanged", "预览内容已变化。", 409);
            if (request.Kind is not ("unmanage" or "undo-quarantine" or "finalize-purge")) service.CheckSource(instance);
            return Results.Accepted(value: TaskView(service.Store.Enqueue(id, request.Kind, payload, User(context), key, hash)));
        });
        api.MapGet("/tasks/{id}", (string id, SqliteStore store) => Results.Ok(TaskView(store.Task(id))));
        api.MapGet("/instances/{id}/events", async (string id, HttpContext context, SqliteStore store, CancellationToken ct) =>
        {
            store.Instance(id);
            var header = context.Request.Headers["Last-Event-ID"].ToString();
            var after = header.Length == 0 ? 0 : long.TryParse(header, out var parsed) && parsed >= 0 ? parsed
                : throw new PanelException("InvalidEventCursor", "事件游标无效。", 400);
            context.Response.ContentType = "text/event-stream";
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    foreach (var change in store.Events(id, after))
                    {
                        await context.Response.WriteAsync($"id: {change.Sequence}\ndata: {JsonSerializer.Serialize(change, DurableFile.JsonCompact)}\n\n", ct);
                        after = change.Sequence;
                    }
                    await context.Response.WriteAsync(": heartbeat\n\n", ct);
                    await context.Response.Body.FlushAsync(ct);
                    await Task.Delay(TimeSpan.FromSeconds(5), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        });
        api.MapPost("/tasks/{id}/recovery-preview", (string id, RecoveryRequest request, HttpContext context,
            SqliteStore store, ConfirmationTokens tokens) =>
        {
            var task = store.Task(id);
            if (task.State != "NeedsAttention") throw new PanelException("RecoveryStateChanged", "任务不是待处理状态。", 409);
            var instance = store.Instance(task.InstanceId);
            var hash = RecoveryHash(task, instance, request);
            var token = tokens.Issue(User(context), id, request.Resolution, instance.Revision, hash);
            return Results.Ok(new { token.Token, hash, instance.WorldGuid, task.RecoveryPoint, task.SafeCode });
        });
        api.MapPost("/tasks/{id}/recover", async (string id, RecoveryRequest request, HttpContext context,
            SqliteStore store, ConfirmationTokens tokens, TaskRecoveryService recovery) =>
        {
            AdministratorSecurity.RequireRecent(context.User);
            var task = store.Task(id);
            var instance = store.Instance(task.InstanceId);
            var hash = RecoveryHash(task, instance, request);
            if (hash != request.PreviewHash) throw new PanelException("PreviewChanged", "任务已改变，请重新预检。", 409);
            tokens.Consume(request.Confirmation ?? "", User(context), id, request.Resolution, instance.Revision, hash);
            return Results.Ok(TaskView(await recovery.RecoverAsync(id, request.Resolution, request.OriginalPlayersVerified, User(context))));
        });
        api.MapPost("/tasks/{id}/cancel", (string id, HttpContext context, SqliteStore store, InstanceService service) =>
        {
            var task = store.Task(id);
            var instance = store.Instance(task.InstanceId);
            service.CheckRoot(instance);
            using var instanceLock = DiskLock.AcquireInstance(service.Options.StateRoot, instance.Root, instance.Id);
            if (!store.SetTask(id, "Cancelled", "Cancelled", expectedState: "Queued")) throw new PanelException("CancelUnsafe", "执行中的任务只能在安全检查点处理，不能强行取消。", 409);
            store.Audit(User(context), task.InstanceId, "cancel-task", "Succeeded", id);
            return Results.Ok(TaskView(store.Task(id)));
        });
        api.MapPost("/instances/{id}/uploads", async (string id, HttpContext context, InstanceService service, HeavyIoGate ioGate, CancellationToken ct) =>
        {
            AdministratorSecurity.RequireRecent(context.User);
            var instance = service.Store.Instance(id);
            if (!instance.Writable) throw new PanelException("ReadOnlyInstance", "只读实例不能导入。", 409);
            if (context.Request.ContentType != "application/zip") throw new PanelException("InvalidUpload", "请以 application/zip 上传原始 ZIP。", 415);
            var directory = SafePaths.Within(service.Options.StateRoot, "uploads/" + id);
            Directory.CreateDirectory(directory);
            using var uploadLock = DiskLock.Acquire(SafePaths.Within(directory, "upload.lock"));
            if (Directory.EnumerateFiles(directory, "*.zip").Count() >= 3) throw new PanelException("UploadLimit", "每实例最多保留三个上传包。", 409);
            var uploadId = Guid.NewGuid().ToString("N");
            var path = SafePaths.Within(directory, uploadId + ".zip");
            service.CheckDisk(directory, context.Request.ContentLength ?? (4L << 30));
            await ioGate.Semaphore.WaitAsync(ct);
            try
            {
                await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                {
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    var buffer = new byte[81920];
                    long bytes = 0;
                    int count;
                    while ((count = await context.Request.Body.ReadAsync(buffer, ct)) != 0)
                    {
                        bytes += count;
                        if (bytes > (4L << 30)) throw new PanelException("UploadTooLarge", "上传超过 4 GiB。", 413);
                        await stream.WriteAsync(buffer.AsMemory(0, count), ct);
                    }
                    stream.Flush(true);
                }
                var preview = await ZipWorldArchive.InspectAsync(path, new(), ct);
                service.Store.Audit(User(context), id, "upload-preview", "Succeeded");
                return Results.Ok(new { uploadId, preview.Sha256, preview.Worlds, preview.ExpandedBytes, preview.Files, preview.Ignored });
            }
            catch { File.Delete(path); throw; }
            finally { ioGate.Semaphore.Release(); }
        });
        api.MapDelete("/instances/{id}/uploads/{uploadId}", (string id, string uploadId, InstanceService service) =>
        {
            service.Store.Instance(id);
            if (!Guid.TryParseExact(uploadId, "N", out _)) throw new PanelException("InvalidUpload", "上传标识无效。", 400);
            var instance = service.Store.Instance(id);
            service.CheckRoot(instance);
            using var instanceLock = DiskLock.AcquireInstance(service.Options.StateRoot, instance.Root, instance.Id);
            if (service.Store.Tasks("Queued").Concat(service.Store.Tasks("Running")).Concat(service.Store.Tasks("NeedsAttention")).Any(task => task.InstanceId == id && task.Kind == "import"))
                throw new PanelException("UploadInUse", "导入任务仍引用上传包，不能删除。", 409);
            File.Delete(SafePaths.Within(service.Options.StateRoot, $"uploads/{id}/{uploadId}.zip"));
            return Results.NoContent();
        });
    }

    public static object TaskView(TaskRecord t) => new { t.Id, t.InstanceId, t.Kind, t.State, t.Phase,
        t.CreatedUtc, t.SafeCode, t.RecoveryPoint, t.Message };
    private static string PreviewHash(InstanceRecord i, ActionRequest request) => InstanceService.Hash(
        JsonSerializer.Serialize(new { i.Id, i.Revision, i.SourceHash, request.Kind, request.Arguments }, DurableFile.Json));
    private static string RecoveryHash(TaskRecord task, InstanceRecord instance, RecoveryRequest request) => InstanceService.Hash(
        JsonSerializer.Serialize(new { task.Id, task.State, task.Phase, task.RecoveryPoint, instance.Revision,
            request.Resolution, request.OriginalPlayersVerified }, DurableFile.Json));
    private static string User(HttpContext context) => context.User.FindFirstValue(ClaimTypes.Name)!;
    private static string Key(HttpContext context) => context.Request.Headers["Idempotency-Key"].ToString();
    private static void CheckAction(string action)
    { if (!Actions.Contains(action)) throw new PanelException("UnsupportedAction", "操作不受支持。", 400); }
    private static void Match(HttpContext context, InstanceRecord instance)
    {
        if (context.Request.Headers.IfMatch.ToString().Trim('"') != instance.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new PanelException("RevisionConflict", "配置已经变化，请刷新预览。", 412);
    }
}
