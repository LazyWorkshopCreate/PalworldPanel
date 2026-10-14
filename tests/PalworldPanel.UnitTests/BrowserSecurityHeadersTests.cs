using System.Net;
using PalworldPanel.Server.Infrastructure;
using Xunit;

namespace PalworldPanel.UnitTests;

public sealed class BrowserSecurityHeadersTests
{
    [Theory]
    [InlineData("172.30.88.5", true, true)]
    [InlineData("::ffff:172.30.88.5", true, true)]
    [InlineData("172.30.88.5", false, false)]
    [InlineData("172.30.88.10", true, false)]
    [InlineData("192.0.2.11", true, false)]
    [InlineData("127.0.0.1", true, false)]
    [InlineData(null, true, false)]
    public void LinuxAnnotationCompatibilityRequiresExplicitDesktopModeAndExactBrowserBridge(string? ip, bool desktopValidation, bool compatible)
    {
        var policy = BrowserSecurityHeaders.ContentSecurityPolicy("synthetic-nonce", false, ip is null ? null : IPAddress.Parse(ip), desktopValidation);
        Assert.Equal(compatible, policy.Contains("style-src-elem 'self' 'unsafe-inline'", StringComparison.Ordinal));
        Assert.Contains("style-src 'self' 'nonce-synthetic-nonce'", policy);
        Assert.Contains("script-src 'self';", policy);
        Assert.DoesNotContain("unsafe-eval", policy);
    }
    [Theory]
    [InlineData(true, "127.0.0.1", true)]
    [InlineData(true, "::ffff:127.0.0.1", true)]
    [InlineData(true, "192.0.2.11", false)]
    [InlineData(true, "127.0.0.2", false)]
    [InlineData(true, "::1", false)]
    [InlineData(false, "127.0.0.1", false)]
    [InlineData(false, "192.0.2.11", false)]
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
