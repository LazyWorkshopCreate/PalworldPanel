using System.Diagnostics;
using System.Text;
using System.Text.Json;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed record CommandResult(int ExitCode, string Output);

public sealed class DockerBackend
{
    private readonly PanelOptions? options;
    public DockerBackend() { }
    public DockerBackend(PanelOptions options) => this.options = options;
    public async Task<CommandResult> RunAsync(IEnumerable<string> arguments, TimeSpan timeout,
        string? workingDirectory = null, CancellationToken cancellation = default, bool captureLogErrors = false)
    {
        var info = new ProcessStartInfo(options?.DockerExecutable ?? "docker")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory
        };
        if (options?.DockerEndpoint is { } endpoint)
        {
            info.Environment["DOCKER_HOST"] = endpoint;
            info.Environment.Remove("DOCKER_CONTEXT");
            info.Environment.Remove("DOCKER_TLS_VERIFY");
            info.Environment.Remove("DOCKER_CERT_PATH");
        }
        if (options?.DockerConfigDirectory is { } config) info.Environment["DOCKER_CONFIG"] = config;
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new PanelException("DockerUnavailable", "不能启动 Docker 管理命令。", 503);
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        expiry.CancelAfter(timeout);
        var stdout = DrainAsync(process.StandardOutput);
        var stderr = DrainAsync(process.StandardError);
        try { await process.WaitForExitAsync(expiry.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw new PanelException("CommandTimeout", "Docker 命令超时，必须核实实际状态。", 409);
        }
        var output = await stdout;
        // Only the fixed logs adapter may merge stderr, and must redact before returning it.
        var errors = await stderr;
        if (captureLogErrors) output = (output + errors)[..Math.Min(output.Length + errors.Length, 2 * 1024 * 1024)];
        return new(process.ExitCode, output);
    }

    public async Task<JsonElement> InspectAsync(string container, CancellationToken cancellation = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(container, "^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$"))
            throw new PanelException("InvalidContainer", "容器标识无效。", 400);
        var result = await RunAsync(["inspect", container], TimeSpan.FromSeconds(10), cancellation: cancellation);
        if (result.ExitCode != 0) throw new PanelException("ContainerNotFound", "目标容器不存在或不可读取。", 404);
        using var document = JsonDocument.Parse(result.Output);
        return document.RootElement[0].Clone();
    }

    public async Task<JsonElement[]> ContainersAsync(CancellationToken cancellation = default)
    {
        var result = await RunAsync(["ps", "-aq"], TimeSpan.FromSeconds(10), cancellation: cancellation);
        if (result.ExitCode != 0) throw new PanelException("DockerUnavailable", "Docker 不可访问。", 503);
        var values = new List<JsonElement>();
        var ids = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (ids.Length > 200) throw new PanelException("HostInventoryTooLarge", "宿主容器数量超出单服务器预检范围，拒绝忽略端口或资源占用。", 409);
        foreach (var id in ids)
        {
            try { values.Add(await InspectAsync(id.Trim(), cancellation)); }
            catch (PanelException ex) when (ex.Code == "ContainerNotFound") { }
        }
        return values.ToArray();
    }

    public async Task CommandAsync(string[] args, TimeSpan timeout, string? workingDirectory = null,
        CancellationToken cancellation = default)
    {
        var result = await RunAsync(args, timeout, workingDirectory, cancellation);
        if (result.ExitCode != 0) throw new PanelException("DockerCommandFailed", "Docker 操作失败，请核实目标状态。", 409);
    }

    public async Task UpdateInstallationAsync(InstanceRecord instance, string taskId, CancellationToken cancellation)
    {
        if (!Guid.TryParseExact(taskId, "N", out _)) throw new PanelException("InvalidTask", "任务标识无效。", 400);
        var data = SafePaths.Within(instance.Root, "data");
        if (options?.DockerHostRoot is { } host && options.ContainerMountRoot is { } mount)
            data = SafePaths.Within(host, Path.GetRelativePath(mount, data));
        if (data.Contains(',')) throw new PanelException("UnsupportedMount", "实例目录暂不支持逗号，请调整目录后重试。", 409);
        var name = "pp-update-" + taskId;
        try
        {
            await CommandAsync(["run", "--rm", "--pull", "never", "--name", name, "--mount", $"type=bind,source={data},target=/palworld",
                "--env", "SERVER_PLATFORM=Linux", "--env", "USE_DEPOT_DOWNLOADER=true", "--env", "INSTALL_BETA_INSIDER=false",
                "--env", "TARGET_MANIFEST_ID=", "--entrypoint", "/bin/bash", instance.Image, "-c",
                "set -eo pipefail; source /home/steam/server/helper_install.sh; InstallServer"], TimeSpan.FromMinutes(30), cancellation: cancellation);
        }
        finally
        {
            await StopUpdaterAsync(taskId);
        }
    }

    public async Task StopUpdaterAsync(string taskId)
    {
        if (!Guid.TryParseExact(taskId, "N", out _)) throw new PanelException("InvalidTask", "任务标识无效。", 400);
        var name = "pp-update-" + taskId;
        var cleanup = await RunAsync(["rm", "--force", name], TimeSpan.FromSeconds(30), cancellation: CancellationToken.None);
        if (cleanup.ExitCode != 0)
        {
            var remaining = await RunAsync(["ps", "--all", "--quiet", "--filter", "name=^/" + name + "$"], TimeSpan.FromSeconds(10));
            if (remaining.ExitCode != 0 || !string.IsNullOrWhiteSpace(remaining.Output))
                throw new PanelException("UpdaterCleanupFailed", "更新进程未确认退出，请在任务页处理。", 409);
        }
    }

    public async Task ComposeAsync(InstanceRecord instance, string[] arguments, bool hold,
        CancellationToken cancellation = default, bool updateInstallation = false)
    {
        var compose = SafePaths.Within(instance.Root, "compose.yaml");
        var args = new List<string> { "compose", "--project-name", instance.Project, "--file", compose };
        if (hold)
        {
            var path = SafePaths.Within(instance.Root, "transaction.override.json");
            DurableFile.WriteJson(path, new { services = new Dictionary<string, object>
            { [instance.Service] = new { restart = "no", environment = new { UPDATE_ON_BOOT = updateInstallation ? "true" : "false", AUTO_UPDATE_ENABLED = "false", BACKUP_ENABLED = "false" } } } });
            args.AddRange(["--file", path]);
        }
        args.AddRange(arguments);
        await CommandAsync(args.ToArray(), TimeSpan.FromMinutes(30), instance.Root, cancellation);
    }

    public async Task<string?> ContainerIdAsync(InstanceRecord instance, CancellationToken cancellation = default)
    {
        if (instance.QuarantinedUtc is not null) return null;
        if (!instance.Owned && !instance.Writable)
        {
            if (instance.ContainerId is null) return null;
            // Recovery may have removed the previously registered stopped container.
            // Confirm absence with Docker; daemon failures must never imply a safe stop.
            var registered = await RunAsync(["container", "ls", "--all", "--quiet", "--no-trunc", "--filter", "id=" + instance.ContainerId],
                TimeSpan.FromSeconds(10), cancellation: cancellation);
            if (registered.ExitCode != 0) throw new PanelException("DockerUnavailable", "无法核对登记容器，保持未知状态。", 409);
            var current = registered.Output.Trim();
            if (current.Length == 0) return null;
            if (current != instance.ContainerId) throw new PanelException("AmbiguousContainer", "登记容器身份无法核对。", 409);
            return current;
        }
        var result = await RunAsync(["compose", "--project-name", instance.Project, "--file", Path.Combine(instance.Root, "compose.yaml"),
            "ps", "-aq", instance.Service], TimeSpan.FromSeconds(10), instance.Root, cancellation);
        if (result.ExitCode != 0) throw new PanelException("ComposeUnsupported", "无法核对实例 Compose 身份。", 409);
        var id = result.Output.Trim();
        if (id.Contains('\n')) throw new PanelException("AmbiguousContainer", "服务有多个容器，保持只读。", 409);
        return string.IsNullOrEmpty(id) ? null : id;
    }

    public static Dictionary<string, string> EnvironmentValues(JsonElement container) => container.GetProperty("Config").GetProperty("Env")
        .EnumerateArray().Select(e => e.GetString()!).Where(e => e.Contains('='))
        .GroupBy(e => e[..e.IndexOf('=')], StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Last()[(g.Last().IndexOf('=') + 1)..], StringComparer.Ordinal);

    private static async Task<string> DrainAsync(StreamReader reader)
    {
        var output = new StringBuilder();
        var buffer = new char[8192];
        int count;
        while ((count = await reader.ReadAsync(buffer)) != 0)
        {
            if (output.Length < 2 * 1024 * 1024) output.Append(buffer, 0, Math.Min(count, 2 * 1024 * 1024 - output.Length));
        }
        return output.ToString();
    }
}
