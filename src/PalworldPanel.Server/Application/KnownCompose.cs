using System.Text.Json.Nodes;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.Server.Application;

public static class KnownCompose
{
    public static JsonObject Read(InstanceRecord instance, PanelOptions options)
    {
        try
        {
            var document = JsonNode.Parse(File.ReadAllText(SafePaths.Within(instance.Root, "compose.yaml")))?.AsObject();
            if (document is null || document.Any(p => p.Key is not ("services" or "name" or "version") && !p.Key.StartsWith("x-", StringComparison.Ordinal))) throw Unsupported();
            var services = document?["services"]?.AsObject();
            if (services is null || services.Count != 1 || services[instance.Service] is not JsonObject service)
                throw Unsupported();
            if (!options.AllowedImages.Contains(service["image"]?.GetValue<string>() ?? "", StringComparer.Ordinal)) throw Unsupported();
            var supported = new[] { "image", "restart", "stop_grace_period", "cpus", "mem_limit", "env_file", "ports", "volumes", "labels", "logging", "healthcheck" };
            if (service.Any(p => !supported.Contains(p.Key, StringComparer.Ordinal) && !p.Key.StartsWith("x-", StringComparison.Ordinal))) throw Unsupported();
            if (new[] { "command", "entrypoint", "privileged", "network_mode", "environment", "extends", "profiles" }.Any(service.ContainsKey)) throw Unsupported();
            var files = service["env_file"]?.AsArray();
            if (files is null || files.Count != 2 || files[0]?["path"]?.GetValue<string>() != "settings.env" ||
                files[1]?["path"]?.GetValue<string>() != "secrets.env" || files.Any(f => f?["format"]?.GetValue<string>() != "raw")) throw Unsupported();
            EnvironmentFile.ParseRaw(File.ReadAllText(SafePaths.Within(instance.Root, "settings.env")));
            EnvironmentFile.ParseRaw(File.ReadAllText(SafePaths.Within(instance.Root, "secrets.env")));
            var volumes = service["volumes"]?.AsArray();
            if (volumes is null || volumes.Count != 1 || volumes[0]?["type"]?.GetValue<string>() != "bind" ||
                volumes[0]?["target"]?.GetValue<string>() != "/palworld") throw Unsupported();
            var expectedSource = Path.Combine(instance.Root, "data");
            if (options.DockerHostRoot is not null && options.ContainerMountRoot is not null)
                expectedSource = SafePaths.Within(options.DockerHostRoot, Path.GetRelativePath(options.ContainerMountRoot, expectedSource));
            if (volumes[0]?["source"]?.GetValue<string>() != expectedSource) throw Unsupported();
            var ports = service["ports"]?.AsArray()?.Select(p => p?.GetValue<string>()).ToArray();
            var gameAddress = options.DesktopValidation ? "127.0.0.1" : options.BindIp;
            var expectedPorts = new[] { $"{gameAddress}:{instance.GamePort}:8211/udp", $"127.0.0.1:{instance.RestPort}:8212/tcp", $"{gameAddress}:{instance.QueryPort}:27015/udp" };
            if (ports is null || !ports.Order().SequenceEqual(expectedPorts.Order())) throw Unsupported();
            return document!;
        }
        catch (PanelException) { throw; }
        catch (Exception error) when (error is System.Text.Json.JsonException or InvalidOperationException or IOException)
        { throw Unsupported(); }
    }

    public static void Patch(InstanceRecord instance, PanelOptions options)
    {
        var document = Read(instance, options);
        var service = document["services"]![instance.Service]!;
        service["image"] = instance.Image;
        service["cpus"] = instance.Desired.Cpu;
        service["mem_limit"] = instance.Desired.MemoryMiB + "m";
        service["restart"] = "unless-stopped";
        service["stop_grace_period"] = "120s";
        DurableFile.WriteJson(SafePaths.Within(instance.Root, "compose.yaml"), document);
    }
    private static PanelException Unsupported() => new("ComposeUnsupported", "仅已验证的单服务 JSON Compose/raw env_file 模板支持写接管；其他配置保持只读。", 409);
}
