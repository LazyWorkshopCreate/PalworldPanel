using System.Net;
using PalworldPanel.Server.Domain;

namespace PalworldPanel.Server.Infrastructure;

public sealed class SourceAccessPolicy
{
    private readonly HashSet<IPAddress> addresses;

    public SourceAccessPolicy(IEnumerable<string> allowed, bool allowLocalhost = false)
    {
        addresses = [];
        foreach (var text in allowed)
        {
            if (!IPAddress.TryParse(text, out var ip)) throw new PanelException("InvalidAccessPolicy", "白名单只能包含单个内网 IP。", 503);
            ip = Normalize(ip);
            if (!IsPrivate(ip)) throw new PanelException("InvalidAccessPolicy", "白名单不允许公网、回环或 IPv6 地址。", 503);
            addresses.Add(ip);
        }
        if (addresses.Count == 0) throw new PanelException("InvalidAccessPolicy", "白名单为空，拒绝监听。", 503);
        if (allowLocalhost) addresses.Add(IPAddress.Loopback);
    }

    public bool Allows(IPAddress? address) => address is not null && addresses.Contains(Normalize(address));
    public static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    public static bool IsPrivate(IPAddress address)
    {
        address = Normalize(address);
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168;
    }
}
