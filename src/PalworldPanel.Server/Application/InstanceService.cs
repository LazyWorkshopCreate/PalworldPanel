using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed record CreationPlan(string Id, string Root, string BackupRoot, int GamePort, int RestPort,
    int QueryPort, string BackupTime, string Image, GameRules Rules, int? CloneRevision);

public sealed class InstanceService(PanelOptions options, SqliteStore store, SecretVault vault, DockerBackend docker,
    GameRestClient game, HostMetrics hostMetrics)
{
    private readonly SemaphoreSlim allocation = new(1);
    public SqliteStore Store => store;
    public PanelOptions Options => options;

    public async Task ValidateBudgetAsync(InstanceRecord current, GameRules rules, CancellationToken cancellation)
    {
        if (rules.MemoryMiB == current.Desired.MemoryMiB && rules.Cpu == current.Desired.Cpu) return;
        if (current.Owned && rules.MemoryMiB < options.MinimumMemoryMiB) throw new PanelException("MemoryBelowMinimum", "实例内存低于最低预算。", 400);
        if (current.Owned && !options.DesktopValidation && rules.Cpu < 4) throw new PanelException("CpuBelowMinimum", "新实例至少配置 4 CPU。", 400);
        if (rules.MemoryMiB <= current.Desired.MemoryMiB && rules.Cpu <= current.Desired.Cpu) return;
        var info = await docker.RunAsync(["info", "--format", "{{.MemTotal}} {{.NCPU}}"], TimeSpan.FromSeconds(10), cancellation: cancellation);
        if (info.ExitCode != 0) throw new PanelException("DockerUnavailable", "Docker 不可访问。", 503);
        var values = info.Output.Trim().Split(' ');
        var others = store.Instances().Where(i => i.Id != current.Id && i.QuarantinedUtc is null && i.ResourceBudgetKnown).ToArray();
        var external = await ExternalBudgetAsync(cancellation);
        if (!options.DesktopValidation && external.UnlimitedRunning > 0)
            throw new PanelException("ResourceBudgetUnknown", "存在未设置内存上限的外部容器，容量不能可靠预检。", 409);
        if (others.Sum(i => i.Desired.MemoryMiB) + external.Memory + rules.MemoryMiB > long.Parse(values[0], CultureInfo.InvariantCulture) / 1024 / 1024 - options.ReservedMemoryMiB ||
            others.Sum(i => i.Desired.Cpu) + external.Cpu + rules.Cpu > Math.Max(1, int.Parse(values[1], CultureInfo.InvariantCulture) - 2))
            throw new PanelException("ResourceBudgetExceeded", "实例承诺资源超过宿主预算。", 409);
    }

    public async Task<object> HostAsync(CancellationToken cancellation = default)
    {
        var info = await docker.RunAsync(["info", "--format", "{{.MemTotal}} {{.NCPU}}"], TimeSpan.FromSeconds(10), cancellation: cancellation);
        if (info.ExitCode != 0) throw new PanelException("DockerUnavailable", "Docker 主机不可访问。", 503);
        var values = info.Output.Trim().Split(' ');
        var memory = long.Parse(values[0], CultureInfo.InvariantCulture) / 1024 / 1024;
        var cpu = int.Parse(values[1], CultureInfo.InvariantCulture);
        var external = await ExternalBudgetAsync(cancellation);
        var measured = hostMetrics.Sample();
        return new { memoryMiB = memory, cpuCount = cpu, reservedMemoryMiB = options.ReservedMemoryMiB,
            measured.UsedMemoryBytes, measured.AvailableMemoryBytes, measured.CpuPercent, systemSampleUtc = measured.UpdatedUtc,
            systemMemoryBytes = measured.TotalMemoryBytes, systemCpuCount = measured.CpuCount,
            committedMemoryMiB = store.Instances().Where(i => i.QuarantinedUtc is null && i.ResourceBudgetKnown).Sum(i => i.Desired.MemoryMiB) + external.Memory,
            committedCpu = store.Instances().Where(i => i.QuarantinedUtc is null && i.ResourceBudgetKnown).Sum(i => i.Desired.Cpu) + external.Cpu,
            externalMemoryMiB = external.Memory, externalCpu = external.Cpu, external.UnlimitedRunning,
            updatedUtc = DateTimeOffset.UtcNow };
    }

    public static string CreationHash(GameRules rules, string? cloneId, CreationPlan plan) =>
        Hash(JsonSerializer.Serialize(new { rules, cloneId, plan }, DurableFile.Json));

    public async Task<CreationPlan> PreviewCreationAsync(GameRules rules, string? cloneId, CancellationToken cancellation)
    {
        await allocation.WaitAsync(cancellation);
        try
        {
            using var budgetLock = DiskLock.Acquire(Path.Combine(options.StateRoot, "allocation.lock"));
            return await BuildCreationPlanAsync(rules, cloneId, Guid.NewGuid().ToString("N"), cancellation);
        }
        finally { allocation.Release(); }
    }

    private async Task<CreationPlan> BuildCreationPlanAsync(GameRules rules, string? cloneId, string id, CancellationToken cancellation)
    {
        rules.Validate();
        if (!Guid.TryParseExact(id, "N", out _)) throw new PanelException("InvalidPreview", "新建身份无效。", 400);
        if (!options.DesktopValidation && rules.Cpu < 4) throw new PanelException("CpuBelowMinimum", "新实例至少配置 4 CPU。", 400);
        if (rules.MemoryMiB < options.MinimumMemoryMiB) throw new PanelException("MemoryBelowMinimum", "实例内存低于配置最低预算。", 400);
        var info = await docker.RunAsync(["info", "--format", "{{.MemTotal}} {{.NCPU}}"], TimeSpan.FromSeconds(10), cancellation: cancellation);
        if (info.ExitCode != 0) throw new PanelException("DockerUnavailable", "Docker 不可访问。", 503);
        var host = info.Output.Trim().Split(' ');
        var memory = long.Parse(host[0], CultureInfo.InvariantCulture) / 1024 / 1024;
        var cpu = int.Parse(host[1], CultureInfo.InvariantCulture);
        var external = await ExternalBudgetAsync(cancellation);
        if (!options.DesktopValidation && external.UnlimitedRunning > 0)
            throw new PanelException("ResourceBudgetUnknown", "存在未设置内存上限的外部容器，请先核实预算。", 409);
        if (store.Instances().Where(i => i.QuarantinedUtc is null && i.ResourceBudgetKnown).Sum(i => i.Desired.MemoryMiB) + external.Memory + rules.MemoryMiB > memory - options.ReservedMemoryMiB ||
            store.Instances().Where(i => i.QuarantinedUtc is null && i.ResourceBudgetKnown).Sum(i => i.Desired.Cpu) + external.Cpu + rules.Cpu > Math.Max(1, cpu - 2))
            throw new PanelException("ResourceBudgetExceeded", "实例承诺资源超过宿主预算。", 409);
        var root = SafePaths.Within(options.InstanceRoots[0], id);
        SafePaths.EnsureIndependent(root, store.Instances().Select(i => i.Root));
        if (Directory.Exists(root)) throw new PanelException("InstanceRootExists", "目标目录已经存在。", 409);
        // Reject storage failures before allocating a durable instance or create task.
        CheckDisk(root, 20L << 30);
        var ports = await ReservedPortsAsync(cancellation);
        var gamePort = NextPort(options.GamePortStart, ports);
        var restPort = NextPort(options.RestPortStart, ports);
        var queryPort = NextPort(options.QueryPortStart, ports);
        int? cloneRevision = null;
        if (cloneId is not null)
        {
            var source = store.Instance(cloneId);
            if (!source.Owned && source.SourceHash is null) throw new PanelException("UnknownCloneSource", "源配置尚未核实，不能克隆默认值。", 409);
            rules = source.Desired with { Name = rules.Name, Cpu = rules.Cpu, MemoryMiB = rules.MemoryMiB };
            cloneRevision = source.Revision;
        }
        return new(id, root, SafePaths.Within(options.BackupRoot, id), gamePort, restPort, queryPort,
            store.Instances().Count % 2 == 0 ? "05:00" : "05:15", options.DefaultImage, rules, cloneRevision);
    }

    public async Task<TaskRecord> CreateAsync(GameRules rules, string? cloneId, CreationPlan plan, string user, string key,
        CancellationToken cancellation = default)
    {
        rules.Validate();
        if (!options.DesktopValidation && rules.Cpu < 4) throw new PanelException("CpuBelowMinimum", "新实例至少配置 4 CPU。", 400);
        if (rules.MemoryMiB < options.MinimumMemoryMiB) throw new PanelException("MemoryBelowMinimum", "实例内存低于配置最低预算。", 400);
        await allocation.WaitAsync(cancellation);
        try
        {
            using var budgetLock = DiskLock.Acquire(Path.Combine(options.StateRoot, "allocation.lock"));
            var requestHash = CreationHash(rules, cloneId, plan);
            var existing = store.FindRequest(user, key);
            if (existing is not null)
            {
                if (existing.RequestHash != requestHash) throw new PanelException("IdempotencyConflict", "重复请求内容不同。", 409);
                return existing;
            }
            if (JsonSerializer.Serialize(await BuildCreationPlanAsync(rules, cloneId, plan.Id, cancellation), DurableFile.JsonCompact) !=
                JsonSerializer.Serialize(plan, DurableFile.JsonCompact))
                throw new PanelException("PreviewChanged", "目录、端口、规则或备份计划已经变化，请重新预检。", 409);
            var id = plan.Id;
            var instance = new InstanceRecord(id, plan.Rules.Name, plan.Root, "pp-" + id, "palworld", plan.Image,
                plan.GamePort, plan.RestPort, plan.QueryPort, vault.Seal(NewSecret(), id + ":admin"), vault.Seal(NewSecret(), id + ":game"),
                plan.Rules, Writable: true, Owned: true, BackupTime: plan.BackupTime);
            Directory.CreateDirectory(plan.Root);
            store.SaveInstance(instance);
            try { return store.Enqueue(id, "create", "{}", user, key, requestHash); }
            catch { store.ForgetInstance(id); throw; }
        }
        finally { allocation.Release(); }
    }

    public void WriteTemplate(InstanceRecord instance)
    {
        if (!options.AllowedImages.Contains(instance.Image, StringComparer.Ordinal))
            throw new PanelException("UnsupportedTemplate", "模板或镜像未批准。", 409);
        if (!instance.Owned)
        {
            KnownCompose.Patch(instance, options);
            WriteSettings(instance);
            WriteSecrets(instance);
            return;
        }
        Directory.CreateDirectory(SafePaths.Within(instance.Root, "data"));
        var service = new Dictionary<string, object>
        {
            ["image"] = instance.Image, ["restart"] = "unless-stopped", ["stop_grace_period"] = "120s",
            ["cpus"] = instance.Desired.Cpu, ["mem_limit"] = instance.Desired.MemoryMiB + "m",
            ["env_file"] = new[] { new { path = "settings.env", format = "raw" }, new { path = "secrets.env", format = "raw" } },
            ["ports"] = new[] { $"{(options.DesktopValidation ? "127.0.0.1" : options.BindIp)}:{instance.GamePort}:8211/udp", $"127.0.0.1:{instance.RestPort}:8212/tcp", $"{(options.DesktopValidation ? "127.0.0.1" : options.BindIp)}:{instance.QueryPort}:27015/udp" },
            ["volumes"] = new[] { new { type = "bind", source = HostPath(Path.Combine(instance.Root, "data")), target = "/palworld" } },
            ["labels"] = new Dictionary<string, string> { ["com.palworldpanel.instance"] = instance.Id }
        };
        DurableFile.WriteJson(Path.Combine(instance.Root, "compose.yaml"), new { services = new Dictionary<string, object> { [instance.Service] = service } });
        WriteSettings(instance);
        WriteSecrets(instance);
    }

    private void WriteSecrets(InstanceRecord instance)
    {
        var path = SafePaths.Within(instance.Root, "secrets.env");
        var source = File.Exists(path) ? File.ReadAllText(path) : "";
        DurableFile.Write(path, Encoding.UTF8.GetBytes(EnvironmentFile.Patch(source, new Dictionary<string, string>
        { ["ADMIN_PASSWORD"] = EnvironmentFile.IniText(vault.Open(instance.AdminCipher, instance.Id + ":admin")), ["SERVER_PASSWORD"] = EnvironmentFile.IniText(vault.Open(instance.GameCipher, instance.Id + ":game")) })));
    }

    public void WriteSettings(InstanceRecord instance)
    {
        var rules = instance.Desired;
        var values = new Dictionary<string, string>
        {
            ["SERVER_NAME"] = EnvironmentFile.IniText(rules.Name), ["SERVER_DESCRIPTION"] = EnvironmentFile.IniText(rules.Description),
            ["PLAYERS"] = rules.MaxPlayers.ToString(CultureInfo.InvariantCulture), ["DEATH_PENALTY"] = rules.DeathPenalty,
            ["ENABLE_NON_LOGIN_PENALTY"] = rules.OfflinePenalty ? "true" : "false",
            ["BUILD_OBJECT_DETERIORATION_DAMAGE_RATE"] = rules.DeteriorationRate.ToString(CultureInfo.InvariantCulture),
            ["BUILD_OBJECT_DAMAGE_RATE"] = rules.AttackDamageRate.ToString(CultureInfo.InvariantCulture),
            ["PORT"] = "8211", ["REST_API_PORT"] = "8212", ["REST_API_ENABLED"] = "true", ["RCON_ENABLED"] = "false",
            ["UPDATE_ON_BOOT"] = "false", ["AUTO_UPDATE_ENABLED"] = "false", ["BACKUP_ENABLED"] = "false",
            ["AUTO_REBOOT_ENABLED"] = "false", ["COMMUNITY"] = "false", ["DISABLE_GENERATE_SETTINGS"] = rules.Additional is { Count: > 0 } ? "true" : "false",
            ["TZ"] = "Asia/Shanghai", ["PUID"] = "1000", ["PGID"] = "1000"
        };
        var path = SafePaths.Within(instance.Root, "settings.env");
        var source = File.Exists(path) ? File.ReadAllText(path) : "# PalworldPanel managed settings; raw env_file format\n";
        DurableFile.Write(path, Encoding.UTF8.GetBytes(EnvironmentFile.Patch(source, values)));
        if (rules.Additional is { Count: > 0 }) GameSettingsFile.Write(instance, vault);
    }

    public async Task<object> AddressViewAsync(InstanceRecord instance, CancellationToken cancellation)
    {
        string? address = null;
        try
        {
            var id = await docker.ContainerIdAsync(instance, cancellation);
            if (id is not null) address = GameAddress.FromContainer(await docker.InspectAsync(id, cancellation), instance.GamePort, options.BindIp);
        }
        catch (PanelException) { }
        return View(instance, address);
    }

    public static object View(InstanceRecord i, string? gameAddress = null) => new { i.Id, i.Name, i.Project, i.Service, i.Image,
        gameAddress,
        gamePort = i.GamePort > 0 ? (int?)i.GamePort : null,
        restPort = i.RestPort > 0 ? (int?)i.RestPort : null,
        queryPort = i.QueryPort > 0 ? (int?)i.QueryPort : null, i.Revision, i.Writable, i.Owned, i.WorldGuid,
        i.Desired, i.Applied, i.BackupTime, i.RetentionDays, i.DesiredPower, i.GameBuild, i.QuarantinedUtc, i.PurgedUtc,
        configurationKnown = i.Owned || i.SourceHash is not null,
        i.ResourceBudgetKnown,
        gamePasswordConfigured = SecretVault.IsConfigured(i.GameCipher), administratorConfigured = SecretVault.IsConfigured(i.AdminCipher) };

    private sealed class Sample
    {
        public SemaphoreSlim Gate { get; } = new(1);
        public DateTimeOffset SampledUtc;
        public JsonNode? Value;
    }
    private readonly ConcurrentDictionary<string, Sample> samples = new(StringComparer.Ordinal);
    public void InvalidateObservation(string id) => samples.TryRemove(id, out _);

    public async Task<object> ObserveAsync(InstanceRecord instance, CancellationToken cancellation = default)
    {
        var sample = samples.GetOrAdd(instance.Id, _ => new Sample());
        await sample.Gate.WaitAsync(cancellation);
        try
        {
            if (sample.Value is not null && DateTimeOffset.UtcNow - sample.SampledUtc < TimeSpan.FromSeconds(5)) return sample.Value.DeepClone();
            try
            {
                sample.Value = JsonSerializer.SerializeToNode(await SampleAsync(instance, cancellation), DurableFile.Json)!;
                sample.SampledUtc = DateTimeOffset.UtcNow;
                return sample.Value.DeepClone();
            }
            catch (Exception error) when (error is PanelException or HttpRequestException or TaskCanceledException)
            {
                if (sample.Value is null) return new { container = "unknown", gameApi = "unknown", stale = true,
                    updatedUtc = (DateTimeOffset?)null, players = (int?)null, fps = (double?)null, cpu = (double?)null, memoryBytes = (long?)null };
                var retained = sample.Value.DeepClone();
                retained["stale"] = true;
                return retained;
            }
        }
        finally { sample.Gate.Release(); }
    }

    private async Task<object> SampleAsync(InstanceRecord instance, CancellationToken cancellation)
    {
        var id = await docker.ContainerIdAsync(instance, cancellation);
        var state = "stopped";
        var gameApi = "unreachable";
        JsonElement? metrics = null;
        JsonElement? info = null;
        double? cpu = null;
        long? memoryBytes = null;
        object? udp = null;
        var containerUdp = "unknown";
        var hostUdp = "unknown";
        if (OperatingSystem.IsLinux())
        {
            try { hostUdp = UdpSocketPresent(File.ReadAllText("/proc/net/udp") + File.ReadAllText("/proc/net/udp6"), instance.GamePort) ? "observed" : "not-observed"; }
            catch (IOException) { }
        }
        if (id is not null)
        {
            var inspect = await docker.InspectAsync(id, cancellation);
            state = inspect.GetProperty("State").GetProperty("Status").GetString() ?? "unknown";
            if (inspect.GetProperty("NetworkSettings").GetProperty("Ports").TryGetProperty("8211/udp", out var binding) && binding.ValueKind == JsonValueKind.Array)
                udp = binding.EnumerateArray().Select(p => new { address = p.GetProperty("HostIp").GetString(), port = p.GetProperty("HostPort").GetString() }).ToArray();
            if (state == "running")
            {
                var sockets = await docker.RunAsync(["exec", id, "cat", "/proc/net/udp", "/proc/net/udp6"], TimeSpan.FromSeconds(5), cancellation: cancellation);
                if (sockets.ExitCode == 0) containerUdp = UdpSocketPresent(sockets.Output, 8211) ? "observed" : "not-observed";
                var stats = await docker.RunAsync(["stats", "--no-stream", "--format", "{{.CPUPerc}}|{{.MemUsage}}", id], TimeSpan.FromSeconds(10), cancellation: cancellation);
                if (stats.ExitCode == 0)
                {
                    var fields = stats.Output.Trim().Split('|');
                    if (fields.Length == 2 && double.TryParse(fields[0].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var usage)) cpu = usage;
                    if (fields.Length == 2) memoryBytes = ParseMemory(fields[1].Split('/')[0].Trim());
                }
                try
                {
                    info = await game.ReadAsync(instance, "info", cancellation);
                    metrics = await game.ReadAsync(instance, "metrics", cancellation);
                    gameApi = "healthy";
                }
                catch (PanelException error) { gameApi = error.Code == "GameUnauthorized" ? "unauthorized" : "unreachable"; }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
            }
        }
        return new { container = state, gameApi, gameEndpoint = "unknown", updatedUtc = DateTimeOffset.UtcNow,
            worldGuid = Field(info, "worldguid"), gameBuild = Field(info, "version"),
            players = Field(metrics, "currentplayernum"), fps = Field(metrics, "serverfps"),
            days = Field(metrics, "days"), bases = Field(metrics, "basecampnum"),
            cpu, memoryBytes, publishedUdp = udp, udpEvidence = udp is null ? "unknown" : "mapping-observed",
            containerUdp, hostUdp, lanClient = "not-tested",
            source = "Docker inspect / stats / game REST / Linux UDP sockets", stale = false };
    }

    public static bool UdpSocketPresent(string table, int port) => table.Split('\n').Any(line =>
    {
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 1 && fields[1].EndsWith(":" + port.ToString("X4", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    });

    private static long? ParseMemory(string value)
    {
        foreach (var item in new (string Unit, double Scale)[] { ("GiB", 1L << 30), ("MiB", 1L << 20), ("KiB", 1024), ("GB", 1_000_000_000), ("MB", 1_000_000), ("kB", 1000), ("B", 1) })
            if (value.EndsWith(item.Unit, StringComparison.Ordinal) && double.TryParse(value[..^item.Unit.Length], NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= 0)
                return (long)(number * item.Scale);
        return null;
    }

    public string SourceHash(InstanceRecord instance)
    {
        var bytes = new List<byte>();
        foreach (var file in new[] { "compose.yaml", "settings.env", "secrets.env" })
        {
            var path = SafePaths.Within(instance.Root, file);
            if (!File.Exists(path)) throw new PanelException("SourceMissing", "配置源缺失，保持只读。", 409);
            bytes.AddRange(File.ReadAllBytes(path));
            bytes.Add(0);
        }
        var settings = EnvironmentFile.ParseRaw(File.ReadAllText(SafePaths.Within(instance.Root, "settings.env")));
        if (settings.GetValueOrDefault("DISABLE_GENERATE_SETTINGS")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            var gameConfig = SafePaths.Within(instance.Root, GameSettingsFile.RelativePath);
            if (!File.Exists(gameConfig)) throw new PanelException("SourceMissing", "世界配置源缺失。", 409);
            bytes.AddRange(File.ReadAllBytes(gameConfig));
        }
        return Convert.ToHexString(SHA256.HashData(bytes.ToArray()));
    }

    public void CheckSource(InstanceRecord instance)
    {
        CheckRoot(instance);
        if (instance.SourceHash is null || SourceHash(instance) != instance.SourceHash)
            throw new PanelException("SourceDrift", "配置源已变化，请重新核实接管。", 409);
    }
    public void CheckRoot(InstanceRecord instance) => SafePaths.EnsureApproved(instance.Root, options.InstanceRoots);

    public void CheckDisk(string path, long requiredBytes)
    {
        var volume = new DriveInfo(Path.GetFullPath(path));
        var margin = Math.Max(10L << 30, volume.TotalSize / 10);
        if (volume.AvailableFreeSpace - requiredBytes < margin) throw new PanelException("InsufficientStorage", "磁盘不足以保留恢复点与安全余量。", 507);
    }

    private string HostPath(string path)
    {
        if (options.DockerHostRoot is null || options.ContainerMountRoot is null) return path;
        var relative = Path.GetRelativePath(options.ContainerMountRoot, path);
        return SafePaths.Within(options.DockerHostRoot, relative);
    }

    private async Task<HashSet<int>> ReservedPortsAsync(CancellationToken cancellation)
    {
        var ports = store.Instances().Where(i => i.QuarantinedUtc is null).SelectMany(i => new[] { i.GamePort, i.RestPort, i.QueryPort }).ToHashSet();
        foreach (var container in await docker.ContainersAsync(cancellation))
        {
            if (!container.TryGetProperty("HostConfig", out var host) || !host.TryGetProperty("PortBindings", out var bindings)) continue;
            foreach (var binding in bindings.EnumerateObject())
            {
                if (binding.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var entry in binding.Value.EnumerateArray())
                    if (int.TryParse(entry.GetProperty("HostPort").GetString(), out var port)) ports.Add(port);
            }
        }
        if (OperatingSystem.IsLinux())
        {
            foreach (var file in new[] { "/proc/net/tcp", "/proc/net/tcp6", "/proc/net/udp", "/proc/net/udp6" })
                if (File.Exists(file)) foreach (var line in File.ReadLines(file).Skip(1))
                {
                    var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (fields.Length > 1 && int.TryParse(fields[1].Split(':').Last(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var port)) ports.Add(port);
                }
        }
        if (OperatingSystem.IsWindows())
        {
            var network = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
            foreach (var endpoint in network.GetActiveTcpListeners().Concat(network.GetActiveUdpListeners())) ports.Add(endpoint.Port);
        }
        return ports;
    }
    private async Task<(long Memory, double Cpu, int UnlimitedRunning)> ExternalBudgetAsync(CancellationToken cancellation)
    {
        var managed = store.Instances();
        long memory = 0;
        double cpu = 0;
        var unlimited = 0;
        foreach (var container in await docker.ContainersAsync(cancellation))
        {
            var id = container.GetProperty("Id").GetString();
            var labels = container.GetProperty("Config").GetProperty("Labels");
            if (labels.ValueKind == JsonValueKind.Object && labels.TryGetProperty("com.palworldpanel.role", out var role) && role.GetString() == "control-plane") continue;
            if (managed.Any(i => i.ResourceBudgetKnown && i.ContainerId == id) || (labels.ValueKind == JsonValueKind.Object &&
                labels.TryGetProperty("com.palworldpanel.instance", out var label) && managed.Any(i => i.ResourceBudgetKnown && i.Id == label.GetString()))) continue;
            if (!container.GetProperty("State").GetProperty("Running").GetBoolean()) continue;
            var host = container.GetProperty("HostConfig");
            var limit = host.GetProperty("Memory").GetInt64();
            memory += limit / 1024 / 1024;
            cpu += host.GetProperty("NanoCpus").GetInt64() / 1_000_000_000d;
            if (limit == 0 || host.GetProperty("NanoCpus").GetInt64() == 0) unlimited++;
        }
        return (memory, cpu, unlimited);
    }
    private static int NextPort(int start, HashSet<int> used)
    {
        for (var port = start; port <= 65535; port++) if (used.Add(port)) return port;
        throw new PanelException("PortPoolExhausted", "没有可用实例端口。", 409);
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    private static object? Field(JsonElement? value, string name) => value.HasValue && value.Value.TryGetProperty(name, out var field)
        ? field.ValueKind == JsonValueKind.Number ? field.GetDouble() : field.ValueKind == JsonValueKind.String ? field.GetString() : null : null;
}
