using Microsoft.AspNetCore.Http;

namespace RoleRadar.Identity.Configuration;

/// <summary>
/// Feature 015 Gate 5 (ADR 0049): the local AppHost runs Identity over loopback HTTP so no certificate has to be trusted.
/// Duende issues its cookies and the ASP.NET Identity application cookie with SameSite=None, and a browser drops a
/// SameSite=None cookie that is not Secure, so sign-in over plain HTTP never completes. SameSite=Lax is enough for that loop
/// (the web app is on the same site, 127.0.0.1). It is chosen only when the issuer is an http: loopback address, which no TLS
/// deployment has; every other case returns <see cref="SameSiteMode.Unspecified"/>, meaning Duende's default stays.
/// </summary>
public static class LoopbackCookiePolicy
{
    public static SameSiteMode Resolve(string? issuerUri)
    {
        if (string.IsNullOrWhiteSpace(issuerUri)
            || !Uri.TryCreate(issuerUri.Trim(), UriKind.Absolute, out var issuer)
            || issuer.Scheme != Uri.UriSchemeHttp)
        {
            return SameSiteMode.Unspecified;
        }

        return issuer.IsLoopback || string.Equals(issuer.Host, "localhost", StringComparison.OrdinalIgnoreCase)
            ? SameSiteMode.Lax
            : SameSiteMode.Unspecified;
    }
}
