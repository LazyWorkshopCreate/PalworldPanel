using System.Text.Json;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;
using PalworldPanel.Server.Infrastructure;
using System.Security.Cryptography;

namespace PalworldPanel.UnitTests;

public sealed class KnownComposeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "panel-compose-" + Guid.NewGuid().ToString("N"));
    public KnownComposeTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(false, "8213:8211/udp", 0, true)]
    [InlineData(true, "8213:8211/udp", 0, false)]
    [InlineData(true, "192.0.2.10:8213:8211/udp", 0, false)]
    [InlineData(false, "192.0.2.10:8213:8211/udp", 0, true)]
    [InlineData(false, "0.0.0.0:8213:8211/udp", 0, true)]
    [InlineData(false, "8213:8213/udp", 0, false)]
    [InlineData(false, "8213:8211/tcp", 0, false)]
    [InlineData(false, "8213:8211/udp", 18415, false)]
    public void AdoptedServersPreserveExplicitLegacyUdpPublication(bool owned, string port, int query, bool accepted)
    {
        var image = "image@sha256:" + new string('a', 64);
        var options = new PanelOptions("192.0.2.10", 18080, ["192.0.2.11"], root, [root], root, "", "", "", "", image, [image]);
        var instance = new InstanceRecord("id", "world", root, "project", "palworld", image, 8213, 8214, query, "", "", new GameRules("world"), Owned: owned);
        File.WriteAllText(Path.Combine(root, "settings.env"), "PORT=8211\n");
        File.WriteAllText(Path.Combine(root, "secrets.env"), "ADMIN_PASSWORD=example\n");
        var service = new Dictionary<string, object>
        {
            ["image"] = image,
            ["env_file"] = new[] { new { path = "settings.env", format = "raw" }, new { path = "secrets.env", format = "raw" } },
            ["ports"] = new[] { port, "127.0.0.1:8214:8212/tcp" },
            ["volumes"] = new[] { new { type = "bind", source = Path.Combine(root, "data"), target = "/palworld" } }
        };
        File.WriteAllText(Path.Combine(root, "compose.yaml"), JsonSerializer.Serialize(new { services = new Dictionary<string, object> { ["palworld"] = service } }));
        if (accepted) Assert.NotNull(KnownCompose.Read(instance, options));
        else Assert.Throws<PanelException>(() => KnownCompose.Read(instance, options));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedTemplateSeparatesGamePublicationFromManagementAndAcceptsPreviousLanTemplate(bool desktop)
    {
        var image = "image@sha256:" + new string('a', 64);
        var key = Path.Combine(root, "key");
        File.WriteAllBytes(key, RandomNumberGenerator.GetBytes(32));
        var vault = new SecretVault(key);
        var options = new PanelOptions("192.0.2.10", 18080, [], root, [root], root, key, "", "", "", image, [image], DesktopValidation: desktop);
        var instance = new InstanceRecord("id", "world", root, "project", "palworld", image, 18211, 18312, 18415,
            vault.Seal("synthetic-admin", "id:admin"), vault.Seal("synthetic-game", "id:game"), new GameRules("world"), Owned: true);
        var service = new InstanceService(options, null!, vault, null!, null!, null!);
        service.WriteTemplate(instance);
        var document = KnownCompose.Read(instance, options);
        var ports = document["services"]!["palworld"]!["ports"]!.AsArray();
        Assert.Equal(new[] { $"{(desktop ? "127.0.0.1" : "0.0.0.0")}:18211:8211/udp", "127.0.0.1:18312:8212/tcp",
            $"{(desktop ? "127.0.0.1" : options.BindIp)}:18415:27015/udp" }, ports.Select(p => p!.GetValue<string>()));
        if (!desktop)
        {
            ports[0] = "192.0.2.10:18211:8211/udp";
            File.WriteAllText(Path.Combine(root, "compose.yaml"), document.ToJsonString());
            KnownCompose.Patch(instance, options);
            Assert.Equal("192.0.2.10:18211:8211/udp", KnownCompose.Read(instance, options)["services"]!["palworld"]!["ports"]![0]!.GetValue<string>());
        }
        // Management access must remain loopback-only even with wildcard game publication.
        ports[1] = "0.0.0.0:18312:8212/tcp";
        File.WriteAllText(Path.Combine(root, "compose.yaml"), document.ToJsonString());
        Assert.Throws<PanelException>(() => KnownCompose.Read(instance, options));
    }
}
