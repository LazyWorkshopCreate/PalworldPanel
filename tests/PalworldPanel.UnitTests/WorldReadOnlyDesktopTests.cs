using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;

namespace PalworldPanel.UnitTests;

public sealed class WorldReadOnlyDesktopTests
{
    [WorldReadOnlyDesktopFact][Trait("Category", "Desktop")]
    public async Task ProductionHttpMiddlewareAndDockerLogsWithSyntheticWorld()
    {
        Assert.True(OperatingSystem.IsWindows());
        var binary = Environment.GetEnvironmentVariable("PANEL_TEST_SERVER") ?? throw new InvalidOperationException("显式指定本机测试发布目录。");
        var bind = Environment.GetEnvironmentVariable("PANEL_TEST_BIND") ?? throw new InvalidOperationException("显式指定本机内网 IPv4。");
        var root = Path.Combine(Path.GetTempPath(), "pp-world-view-desktop-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var key = Path.Combine(root, "key"); File.WriteAllBytes(key, RandomNumberGenerator.GetBytes(32));
        var admin = Path.Combine(root, "admin.json"); AdministratorSecurity.PrepareWebSetup(admin);
        var image = "example/image@sha256:" + new string('a', 64);
        var options = new PanelOptions(bind, port, [bind], Path.Combine(root, "state"), [Path.Combine(root, "instances")], Path.Combine(root, "backups"), key, admin, "", "", image, [image],
            MinimumMemoryMiB: 512, ReservedMemoryMiB: 512, DesktopValidation: true, AllowHttp: true, AllowWebSetup: true,
            DockerEndpoint: "npipe:////./pipe/dockerDesktopLinuxEngine");
        options.Validate();
        var store = new SqliteStore(Path.Combine(options.StateRoot, "panel.db"));
        var vault = new SecretVault(key);
        var docker = new DockerBackend(options);
        var name = "pp-world-view-test-" + Guid.NewGuid().ToString("N");
        var restListener = new TcpListener(IPAddress.Loopback, 0); restListener.Start();
        var restPort = ((IPEndPoint)restListener.LocalEndpoint).Port; restListener.Stop();
        var id = Guid.NewGuid().ToString("N"); var world = new string('a', 32);
        Process? process = null;
        try
        {
            var info = await docker.RunAsync(["info", "--format", "{{.OSType}}"], TimeSpan.FromSeconds(20));
            Assert.Equal("linux", info.Output.Trim());
            var result = await docker.RunAsync(["run", "-d", "--name", name, "--label", "com.palworldpanel.test=world-readonly", "--memory", "128m", "--cpus", "0.5", "python:3-alpine", "python", "-u", "-c", "import time; print('synthetic-admin synthetic-game /home/private/file 192.168.1.99'); print('synthetic safe log'); time.sleep(300)"], TimeSpan.FromSeconds(40));
            Assert.Equal(0, result.ExitCode);
            var rules = new GameRules("synthetic", Additional: new() { ["ExpRate"] = "2" });
            var instance = new InstanceRecord(id, "synthetic", Path.Combine(root, "instances", id), "project", "palworld", image, 18211, restPort, 18415,
                vault.Seal("synthetic-admin", id + ":admin"), vault.Seal("synthetic-game", id + ":game"), rules, rules, WorldGuid: world, ContainerId: result.Output.Trim());
            var worldPath = Path.Combine(instance.Root, "data", "Pal", "Saved", "SaveGames", "0", world);
            Directory.CreateDirectory(Path.Combine(worldPath, "Players"));
            File.WriteAllText(Path.Combine(worldPath, "Level.sav"), "synthetic-world");
            File.WriteAllText(Path.Combine(worldPath, "LevelMeta.sav"), "synthetic-meta");
            File.WriteAllText(Path.Combine(worldPath, "Players", new string('c', 32) + ".sav"), "synthetic-player");
            foreach (var file in new[] { "compose.yaml", "settings.env", "secrets.env" }) File.WriteAllText(Path.Combine(instance.Root, file), "synthetic-admin");
            store.SaveInstance(instance);
            var instances = new InstanceService(options, store, vault, docker, new GameRestClient(vault), new HostMetrics());
            var backup = await new BackupService(options, store, instances).CreateAsync(instance, "manual", false);
            var config = Path.Combine(root, "config.json"); DurableFile.WriteJson(config, options);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(binary); start.ArgumentList.Add("--config"); start.ArgumentList.Add(config);
            process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(60) };
            for (var attempt = 0; ; attempt++)
            {
                try { using var ready = await client.GetAsync("/api/v1/setup"); if (ready.IsSuccessStatusCode) break; }
                catch (HttpRequestException) { }
                if (attempt >= 60 || process.HasExited) throw new InvalidOperationException("隔离面板未就绪。");
                await Task.Delay(250);
            }
            client.DefaultRequestHeaders.Add("Origin", client.BaseAddress.GetLeftPart(UriPartial.Authority));
            var path = "/api/v1/world-view/" + world;
            using (var denied = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            var login = new WorldLoginRequest(world, "synthetic-game");
            using (var cross = new HttpRequestMessage(HttpMethod.Post, "/api/v1/world-view/session") { Content = JsonContent.Create(login) })
            {
                cross.Headers.Add("Origin", "http://other.invalid");
                using var denied = await client.SendAsync(cross); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            }
            using (var wrong = await client.PostAsJsonAsync("/api/v1/world-view/session", login with { Password = "wrong" })) Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
            using (var accepted = await client.PostAsJsonAsync("/api/v1/world-view/session", login))
            {
                Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
                var cookie = accepted.Headers.GetValues("Set-Cookie").Single(); Assert.Contains("httponly", cookie.ToLowerInvariant()); Assert.Contains("samesite=strict", cookie.ToLowerInvariant()); Assert.Contains("path=/api/v1/world-view", cookie);
            }
            using (var detail = await client.GetAsync(path))
            {
                Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
                var content = await detail.Content.ReadAsStringAsync();
                Assert.DoesNotContain("synthetic-game", content); Assert.DoesNotContain("synthetic-admin", content); Assert.DoesNotContain("PublicIP", content); Assert.DoesNotContain("adminCipher", content);
            }
            using (var logs = await client.GetAsync(path + "/logs"))
            {
                Assert.Equal(HttpStatusCode.OK, logs.StatusCode);
                var content = await logs.Content.ReadAsStringAsync(); Assert.Contains("synthetic safe log", content);
                Assert.DoesNotContain("synthetic-admin", content); Assert.DoesNotContain("synthetic-game", content); Assert.DoesNotContain("/home/private", content); Assert.DoesNotContain("192.168.1.99", content);
            }
            foreach (var route in new[] { "/api/v1/instances", "/api/v1/tasks", "/api/v1/audit", "/api/v1/instances/" + id + "/settings" })
            { using var denied = await client.GetAsync(route); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); }
            using (var denied = await client.PostAsJsonAsync("/api/v1/instances/" + id + "/actions", new { kind = "stop" })) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using (var denied = await client.GetAsync("/api/v1/world-view/" + new string('b', 32))) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using (var download = await client.GetAsync(path + "/backups/" + backup.Id))
            {
                Assert.Equal(HttpStatusCode.OK, download.StatusCode); Assert.Equal("application/zip", download.Content.Headers.ContentType!.MediaType);
                using var stream = await download.Content.ReadAsStreamAsync(); using var memory = new MemoryStream(); await stream.CopyToAsync(memory); memory.Position = 0;
                using var zip = new ZipArchive(memory, ZipArchiveMode.Read); Assert.Equal(3, zip.Entries.Count); Assert.All(zip.Entries, e => Assert.EndsWith(".sav", e.FullName));
            }
            using (var deleted = await client.DeleteAsync("/api/v1/world-view/session")) Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            using (var denied = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Empty(store.Tasks()); Assert.Empty(Directory.EnumerateFiles(Path.Combine(options.StateRoot, "exports")));
            if (Environment.GetEnvironmentVariable("PANEL_TEST_BROWSER_READY") is { } readyFile)
            {
                // Optional browser inspection handoff: synthetic world ID and local URL only.
                DurableFile.WriteJson(readyFile, new { baseUrl = $"http://{bind}:{port}", worldId = world });
                for (var attempt = 0; !File.Exists(readyFile + ".done"); attempt++)
                {
                    if (attempt >= 480) throw new InvalidOperationException("浏览器验收未在四分钟内完成。");
                    await Task.Delay(500);
                }
            }
        }
        finally
        {
            if (process is { HasExited: false }) { process.Kill(true); await process.WaitForExitAsync(); }
            process?.Dispose();
            await docker.RunAsync(["container", "rm", "--force", name], TimeSpan.FromSeconds(20));
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, true);
        }
    }
}

public sealed class WorldReadOnlyDesktopFactAttribute : FactAttribute
{
    public WorldReadOnlyDesktopFactAttribute()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("PANEL_TEST_SERVER") is null || Environment.GetEnvironmentVariable("PANEL_TEST_BIND") is null)
            Skip = "需要显式设置 PANEL_TEST_SERVER / PANEL_TEST_BIND 的 Docker Desktop 隔离验收。";
    }
}
