using System.Text.Json;
using PalworldPanel.Server.Application;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.UnitTests;

public sealed class KnownComposeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "panel-compose-" + Guid.NewGuid().ToString("N"));
    public KnownComposeTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    [Theory]
    [InlineData(false, "8213:8211/udp", 0, true)]
    [InlineData(true, "8213:8211/udp", 0, false)]
    [InlineData(true, "192.168.28.226:8213:8211/udp", 0, false)]
    [InlineData(false, "192.168.28.226:8213:8211/udp", 0, true)]
    [InlineData(false, "8213:8213/udp", 0, false)]
    [InlineData(false, "8213:8211/tcp", 0, false)]
    [InlineData(false, "8213:8211/udp", 18415, false)]
    public void AdoptedServersPreserveExplicitLegacyUdpPublication(bool owned, string port, int query, bool accepted)
    {
        var image = "image@sha256:" + new string('a', 64);
        var options = new PanelOptions("192.168.28.226", 18080, ["192.168.28.238"], root, [root], root, "", "", "", "", image, [image]);
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
}
