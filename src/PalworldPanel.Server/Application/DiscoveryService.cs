using System.Text.Json;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public sealed record DiscoveryCandidate(string ContainerId, string Name, string Image, string? Root,
    int GamePort, int RestPort, int QueryPort, bool Registered, string Capability, string Reason);
public sealed record RegisterRequest(string ContainerId);

public sealed class DiscoveryService(PanelOptions options, SqliteStore store, DockerBackend docker, SecretVault vault,
    GameRestClient game, InstanceService instances)
{
    public async Task<DiscoveryCandidate[]> DiscoverAsync(CancellationToken ct)
    {
        var found = new List<DiscoveryCandidate>();
        foreach (var container in await docker.ContainersAsync(ct))
        {
            var mount = container.GetProperty("Mounts").EnumerateArray().FirstOrDefault(m => m.GetProperty("Destination").GetString() == "/palworld");
            if (mount.ValueKind == JsonValueKind.Undefined) continue;
            var source = DockerPaths.LocalPath(mount.GetProperty("Source").GetString()!);
            var root = mount.GetProperty("Type").GetString() == "bind" && source is not null ? Path.GetDirectoryName(source) : null;
            if (root is not null && options.DockerHostRoot is not null && options.ContainerMountRoot is not null &&
                root.StartsWith(options.DockerHostRoot.TrimEnd('/') + "/", StringComparison.Ordinal))
                root = SafePaths.Within(options.ContainerMountRoot, Path.GetRelativePath(options.DockerHostRoot, root));
            var reason = "仅登记观察；未知配置模板不能写入接管。";
            if (root is null) reason = "命名卷不是批准的实例绑定目录。";
            else if (!options.InstanceRoots.Any(allowed => root.StartsWith(Path.GetFullPath(allowed).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            { root = null; reason = "容器目录位于批准根目录之外。"; }
            var id = container.GetProperty("Id").GetString()!;
            found.Add(new(id, container.GetProperty("Name").GetString()!.TrimStart('/'), container.GetProperty("Config").GetProperty("Image").GetString()!, root,
                Port(container, "8211/udp", false), Port(container, "8212/tcp", true), Port(container, "27015/udp", false),
                store.Instances().Any(i => i.ContainerId == id), root is null ? "unavailable" : "read-only", reason));
        }
        return found.ToArray();
    }

    public async Task<InstanceRecord> RegisterAsync(string containerId, string user, CancellationToken ct)
    {
        var candidate = (await DiscoverAsync(ct)).SingleOrDefault(c => c.ContainerId == containerId)
            ?? throw new PanelException("DiscoveryChanged", "容器发现结果已经变化。", 409);
        if (candidate.Root is null || candidate.Registered) throw new PanelException("RegistrationDenied", "实例不能重复登记或目录未经批准。", 409);
        SafePaths.RejectLinks(candidate.Root);
        SafePaths.EnsureIndependent(candidate.Root, store.Instances().Select(i => i.Root));
        var inspect = await docker.InspectAsync(containerId, ct);
        var env = DockerBackend.EnvironmentValues(inspect);
        var limits = inspect.GetProperty("HostConfig");
        var memory = limits.GetProperty("Memory").GetInt64() / 1024 / 1024;
        var cpu = limits.GetProperty("NanoCpus").GetInt64() / 1_000_000_000d;
        var id = Guid.NewGuid().ToString("N");
        var rules = new GameRules(EnvironmentFile.ReadIniText(env.GetValueOrDefault("SERVER_NAME", candidate.Name)),
            Cpu: cpu > 0 ? cpu : 4, MemoryMiB: memory > 0 ? memory : options.MinimumMemoryMiB);
        var instance = new InstanceRecord(id, rules.Name, candidate.Root, "unknown", "unknown", candidate.Image,
            candidate.GamePort, candidate.RestPort, candidate.QueryPort, vault.Seal(EnvironmentFile.ReadIniText(env.GetValueOrDefault("ADMIN_PASSWORD", "")), id + ":admin"),
            vault.Seal(EnvironmentFile.ReadIniText(env.GetValueOrDefault("SERVER_PASSWORD", "")), id + ":game"), rules, ContainerId: containerId,
            ResourceBudgetKnown: memory > 0 && cpu > 0);
        try
        {
            var labels = inspect.GetProperty("Config").GetProperty("Labels");
            var project = labels.GetProperty("com.docker.compose.project").GetString()!;
            var service = labels.GetProperty("com.docker.compose.service").GetString()!;
            if (!System.Text.RegularExpressions.Regex.IsMatch(project, "^[a-z0-9][a-z0-9_-]{0,63}$") ||
                !System.Text.RegularExpressions.Regex.IsMatch(service, "^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,63}$"))
                throw new PanelException("ComposeUnsupported", "项目或服务标识不受支持。", 409);
            var sourceFiles = labels.GetProperty("com.docker.compose.project.config_files").GetString()!;
            if (sourceFiles != Path.Combine(candidate.Root, "compose.yaml") &&
                sourceFiles != Path.Combine(candidate.Root, "compose.yaml") + "," + Path.Combine(candidate.Root, "transaction.override.json"))
                throw new PanelException("ComposeUnsupported", "无法核对原配置源。", 409);
            instance = instance with { Project = project, Service = service };
            KnownCompose.Read(instance, options);
            var settings = await game.ReadAsync(instance, "settings", ct);
            var host = inspect.GetProperty("HostConfig");
            var existingRules = new GameRules(settings.GetProperty("ServerName").GetString()!, settings.GetProperty("ServerDescription").GetString()!,
                settings.GetProperty("ServerPlayerMaxNum").GetInt32(), settings.GetProperty("DeathPenalty").GetString()!,
                settings.GetProperty("bEnableNonLoginPenalty").GetBoolean(), settings.GetProperty("BuildObjectDeteriorationDamageRate").GetDouble(),
                settings.GetProperty("BuildObjectDamageRate").GetDouble(), host.GetProperty("NanoCpus").GetInt64() / 1_000_000_000d,
                host.GetProperty("Memory").GetInt64() / 1024 / 1024,
                GameSettingsFile.Project(settings).Where(p => GameSettingCatalog.Definitions.Any(d => d.Key == p.Key && d.Editable))
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));
            existingRules.Validate();
            var info = await game.ReadAsync(instance, "info", ct);
            instance = instance with { Name = existingRules.Name, Desired = existingRules, Applied = existingRules,
                WorldGuid = info.GetProperty("worldguid").GetString(), GameBuild = info.GetProperty("version").GetString(),
                SourceHash = instances.SourceHash(instance) };
        }
        catch (Exception error) when (error is PanelException or JsonException or KeyNotFoundException or HttpRequestException or TaskCanceledException or InvalidOperationException)
        { instance = instance with { SourceHash = null }; }
        store.SaveInstance(instance);
        store.Audit(user, id, "register-read-only", "Succeeded");
        return instance;
    }

    private static int Port(JsonElement container, string key, bool loopback)
    {
        if (!container.GetProperty("HostConfig").GetProperty("PortBindings").TryGetProperty(key, out var values) || values.ValueKind != JsonValueKind.Array) return 0;
        var entries = values.EnumerateArray().ToArray();
        if (entries.Length != 1 || (loopback && entries[0].GetProperty("HostIp").GetString() != "127.0.0.1")) return 0;
        return int.TryParse(entries[0].GetProperty("HostPort").GetString(), out var port) ? port : 0;
    }
}
