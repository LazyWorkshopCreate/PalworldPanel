using System.Net;

namespace PalworldPanel.Server.Infrastructure;

public static class BrowserSecurityHeaders
{
    public static string ContentSecurityPolicy(string styleNonce, bool windows, IPAddress? source, bool desktopValidation = false)
    {
        // Desktop annotation tools inject style elements without the application's nonce.
        // Keep compatibility scoped to Windows loopback or the explicitly enabled Desktop test browser bridge.
        var address = source is null ? null : SourceAccessPolicy.Normalize(source);
        var compatibleStyles = windows && IPAddress.Loopback.Equals(address) ||
            desktopValidation && IPAddress.Parse("172.30.88.5").Equals(address);
        var localStyles = compatibleStyles
            ? "; style-src-elem 'self' 'unsafe-inline'"
            : "";
        return $"default-src 'self'; style-src 'self' 'nonce-{styleNonce}'{localStyles}; script-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    }
}
