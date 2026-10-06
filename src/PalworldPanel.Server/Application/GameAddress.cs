using System.Net;
using System.Text.Json;

namespace PalworldPanel.Server.Application;

public static class GameAddress
{
    public static string? FromContainer(JsonElement container, int port, string hostAddress)
    {
        JsonElement bindings = default;
        if (container.TryGetProperty("NetworkSettings", out var network) && network.ValueKind == JsonValueKind.Object &&
            network.TryGetProperty("Ports", out var published) && published.ValueKind == JsonValueKind.Object)
            published.TryGetProperty("8211/udp", out bindings);
        if (bindings.ValueKind != JsonValueKind.Array && container.TryGetProperty("HostConfig", out var host) && host.ValueKind == JsonValueKind.Object &&
            host.TryGetProperty("PortBindings", out var configured) && configured.ValueKind == JsonValueKind.Object) configured.TryGetProperty("8211/udp", out bindings);
        if (bindings.ValueKind != JsonValueKind.Array) return null;
        var addresses = new List<string>();
        foreach (var binding in bindings.EnumerateArray())
        {
            if (!binding.TryGetProperty("HostPort", out var hostPort) || !int.TryParse(hostPort.GetString(), out var mapped) || mapped != port ||
                !binding.TryGetProperty("HostIp", out var hostIp) || !IPAddress.TryParse(hostIp.GetString(), out var address)) continue;
            addresses.Add(address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ? hostAddress : address.ToString());
        }
        return addresses.FirstOrDefault(address => address == hostAddress) ?? addresses.FirstOrDefault();
    }
}
