using System.Net;

namespace PalworldPanel.Server.Infrastructure;

public static class BrowserSecurityHeaders
{
    public static string ContentSecurityPolicy(string styleNonce, bool windows, IPAddress? source)
    {
        // Desktop annotation tools inject style elements without the application's nonce.
        // Limit that compatibility allowance to the Windows local loopback listener.
        var localStyles = windows && source is not null && SourceAccessPolicy.Normalize(source).Equals(IPAddress.Loopback)
            ? "; style-src-elem 'self' 'unsafe-inline'"
            : "";
        return $"default-src 'self'; style-src 'self' 'nonce-{styleNonce}'{localStyles}; script-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    }
}
