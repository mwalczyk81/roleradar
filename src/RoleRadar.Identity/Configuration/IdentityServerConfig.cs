using System.Threading.RateLimiting;
using Duende.IdentityServer.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RoleRadar.Identity.Configuration;

public static class IdentityServerConfig
{
    public const string AuthRateLimitPolicy = "auth-rate-limit";

    public static IEnumerable<IdentityResource> GetIdentityResources() =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile()
    ];

    public static IEnumerable<ApiScope> GetApiScopes() =>
    [
        new ApiScope("roleradar.api", "Full access to RoleRadar REST API"),
        new ApiScope("roleradar.api.read", "Read-only access to RoleRadar REST API")
    ];

    public static IEnumerable<ApiResource> GetApiResources() =>
    [
        new ApiResource("roleradar.api", "RoleRadar REST Resource Server")
        {
            Scopes = { "roleradar.api", "roleradar.api.read" }
        }
    ];

    public static IEnumerable<Client> GetClients(IConfiguration configuration)
    {
        var webSecret = configuration["Clients:Web:Secret"]
            ?? Environment.GetEnvironmentVariable("ROLERADAR_WEB_CLIENT_SECRET");
        var headlessSecret = configuration["Clients:Headless:Secret"]
            ?? Environment.GetEnvironmentVariable("ROLERADAR_HEADLESS_CLIENT_SECRET");

        var missingSecrets = new List<string>();
        if (string.IsNullOrWhiteSpace(webSecret))
        {
            missingSecrets.Add("Clients:Web:Secret (or ROLERADAR_WEB_CLIENT_SECRET)");
        }

        if (string.IsNullOrWhiteSpace(headlessSecret))
        {
            missingSecrets.Add("Clients:Headless:Secret (or ROLERADAR_HEADLESS_CLIENT_SECRET)");
        }

        if (missingSecrets.Count > 0)
        {
            var errorMessage = $"[FATAL] RoleRadar.Identity refused to start: required OIDC client secret configuration is missing or empty ({string.Join(", ", missingSecrets)}).";
            Console.Error.WriteLine(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        var headlessSubject = configuration["Clients:Headless:Subject"]
            ?? Environment.GetEnvironmentVariable("ROLERADAR_HEADLESS_ALLOWED_SUBJECT");
        if (string.IsNullOrWhiteSpace(headlessSubject))
        {
            headlessSubject = "roleradar-headless";
        }

        var webRedirectUri = configuration["Clients:Web:RedirectUri"] ?? "https://localhost:5002/signin-oidc";
        var webPostLogoutUri = configuration["Clients:Web:PostLogoutRedirectUri"] ?? "https://localhost:5002/signout-callback-oidc";

        return
        [
            new Client
            {
                ClientId = "roleradar-web",
                ClientName = "RoleRadar Web Application",
                AllowedGrantTypes = GrantTypes.Code,
                RequirePkce = true,
                RequireClientSecret = true,
                ClientSecrets = { new Secret(webSecret!.Trim().Sha256()) },
                RedirectUris = { webRedirectUri },
                PostLogoutRedirectUris = { webPostLogoutUri },
                AllowOfflineAccess = true,
                AllowedScopes = { "openid", "profile", "roleradar.api", "offline_access" },

                // Strict token and code lifetimes per ADR-0017
                AccessTokenLifetime = 600,             // 10 minutes
                AuthorizationCodeLifetime = 60,         // 60 seconds
                AbsoluteRefreshTokenLifetime = 2592000, // 30 days
                SlidingRefreshTokenLifetime = 1296000,  // 15 days
                RefreshTokenUsage = TokenUsage.OneTimeOnly,
                RefreshTokenExpiration = TokenExpiration.Sliding,
                UpdateAccessTokenClaimsOnRefresh = true
            },
            new Client
            {
                ClientId = "roleradar-headless",
                ClientName = "Headless Automation Client",
                AllowedGrantTypes = GrantTypes.ClientCredentials,
                ClientSecrets = { new Secret(headlessSecret!.Trim().Sha256()) },
                AllowedScopes = { "roleradar.api.read" },
                AccessTokenLifetime = 300,             // 5 minutes
                ClientClaimsPrefix = string.Empty,
                Claims = { new ClientClaim("sub", headlessSubject.Trim()) }
            }
        ];
    }

    public static void ConfigureRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(AuthRateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "loopback",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });
    }
}
