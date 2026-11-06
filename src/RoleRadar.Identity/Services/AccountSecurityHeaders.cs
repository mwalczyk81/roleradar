using Duende.IdentityServer.Models;

namespace RoleRadar.Identity.Services;

/// <summary>
/// Response headers for the interactive sign-in pages under <c>/Account</c> (009 US4, ADR 0034): a Content-Security-Policy,
/// <c>X-Content-Type-Options: nosniff</c> and <c>Referrer-Policy: no-referrer</c>. Nothing is added for <c>/connect/*</c>,
/// the discovery document or <c>/health</c>.
/// </summary>
/// <remarks>
/// <c>form-action</c> lists the origins of every registered client's redirect and post-logout URIs. Chromium enforces
/// <c>form-action</c> on the redirect chain that follows a form post, and the sign-in and sign-out posts end at the web app,
/// so a policy of <c>'self'</c> alone would block a legitimate sign-in. The origins are derived from the registered clients
/// (the same configuration <see cref="Configuration.IdentityServerConfig.GetClients"/> reads), never hard-coded.
/// </remarks>
public static class AccountSecurityHeaders
{
    private static readonly PathString AccountPath = new("/Account");

    private const string BasePolicy =
        "default-src 'none'; style-src 'self'; font-src 'self'; script-src 'self'; img-src 'self' data:; " +
        "base-uri 'none'; object-src 'none'; frame-ancestors 'none'; form-action 'self'";

    /// <summary>The CSP string: the fixed policy plus the distinct http(s) origins of every client's redirect and post-logout URIs.</summary>
    public static string BuildContentSecurityPolicy(IEnumerable<Client> clients)
    {
        var origins = clients
            .SelectMany(client => client.RedirectUris.Concat(client.PostLogoutRedirectUris))
            .Select(OriginOf)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);

        return string.Join(' ', new[] { BasePolicy }.Concat(origins));
    }

    /// <summary>Adds the headers to responses under <c>/Account</c> only.</summary>
    public static IApplicationBuilder UseAccountSecurityHeaders(this IApplicationBuilder app, IEnumerable<Client> clients)
    {
        var policy = BuildContentSecurityPolicy(clients);

        return app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(AccountPath, StringComparison.OrdinalIgnoreCase))
            {
                var headers = context.Response.Headers;
                headers.ContentSecurityPolicy = policy;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
            }

            await next();
        });
    }

    private static string? OriginOf(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https"
            ? parsed.GetLeftPart(UriPartial.Authority)
            : null;
}
