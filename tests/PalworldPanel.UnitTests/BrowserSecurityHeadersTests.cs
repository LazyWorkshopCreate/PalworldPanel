using System.Net;
using PalworldPanel.Server.Infrastructure;
using Xunit;

namespace PalworldPanel.UnitTests;

public sealed class BrowserSecurityHeadersTests
{
    [Theory]
    [InlineData(true, "127.0.0.1", true)]
    [InlineData(true, "::ffff:127.0.0.1", true)]
    [InlineData(true, "192.168.28.238", false)]
    [InlineData(true, "127.0.0.2", false)]
    [InlineData(true, "::1", false)]
    [InlineData(false, "127.0.0.1", false)]
    [InlineData(false, "192.168.28.238", false)]
    public void AnnotationStylesAreAllowedOnlyOnWindowsExactLoopback(bool windows, string ip, bool compatible)
    {
        var policy = BrowserSecurityHeaders.ContentSecurityPolicy("synthetic-nonce", windows, IPAddress.Parse(ip));
        Assert.Equal(compatible, policy.Contains("style-src-elem 'self' 'unsafe-inline'", StringComparison.Ordinal));
        Assert.Contains("style-src 'self' 'nonce-synthetic-nonce'", policy);
        Assert.Contains("script-src 'self';", policy);
        Assert.DoesNotContain("script-src 'self' 'unsafe-inline'", policy);
        Assert.DoesNotContain("unsafe-eval", policy);
        Assert.Contains("frame-ancestors 'none'", policy);
    }
}
