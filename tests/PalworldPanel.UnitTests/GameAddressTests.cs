using System.Text.Json;
using PalworldPanel.Server.Application;
using Xunit;

namespace PalworldPanel.UnitTests;

public sealed class GameAddressTests
{
    [Theory]
    [InlineData("192.168.1.10", "18211", "192.168.1.10")]
    [InlineData("127.0.0.1", "18211", "127.0.0.1")]
    [InlineData("0.0.0.0", "18211", "192.168.1.10")]
    [InlineData("::", "18211", "192.168.1.10")]
    [InlineData("192.168.1.10", "18212", null)]
    public void UsesMappedUdpHostAddressWithoutAssumingBrowserLoopback(string address, string port, string? expected)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new {
            NetworkSettings = new { Ports = new Dictionary<string, object> { ["8211/udp"] = new[] { new { HostIp = address, HostPort = port } } } }
        }));
        Assert.Equal(expected, GameAddress.FromContainer(document.RootElement, 18211, "192.168.1.10"));
    }
    [Fact]
    public void StoppedContainerUsesConfiguredBindingAndAbsentMappingIsUnknown()
    {
        using var configured = JsonDocument.Parse("""
            {"NetworkSettings":{"Ports":{}},"HostConfig":{"PortBindings":{"8211/udp":[{"HostIp":"192.168.1.20","HostPort":"18211"}]}}}
            """);
        Assert.Equal("192.168.1.20", GameAddress.FromContainer(configured.RootElement, 18211, "192.168.1.10"));
        using var absent = JsonDocument.Parse("{}");
        Assert.Null(GameAddress.FromContainer(absent.RootElement, 18211, "192.168.1.10"));
        using var empty = JsonDocument.Parse("""{"NetworkSettings":{"Ports":null},"HostConfig":null}""");
        Assert.Null(GameAddress.FromContainer(empty.RootElement, 18211, "192.168.1.10"));
    }
}
