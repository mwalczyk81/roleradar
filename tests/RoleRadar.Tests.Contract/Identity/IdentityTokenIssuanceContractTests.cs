using Duende.IdentityServer.Models;
using Microsoft.Extensions.Configuration;
using RoleRadar.Identity.Configuration;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

public sealed class IdentityTokenIssuanceContractTests
{
    private readonly IConfiguration _configuration;

    public IdentityTokenIssuanceContractTests()
    {
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Clients:Web:Secret"] = "web-client-test-secret-123456789012345",
                ["Clients:Web:RedirectUri"] = "https://localhost:5002/signin-oidc",
                ["Clients:Web:PostLogoutRedirectUri"] = "https://localhost:5002/signout-callback-oidc",
                ["Clients:Headless:Secret"] = "headless-test-secret-123456789012345"
            })
            .Build();
    }

    [Fact]
    public void WebClient_HasExactOidcLifetimesAndPkceEnforcement()
    {
        var clients = IdentityServerConfig.GetClients(_configuration).ToArray();
        var webClient = clients.FirstOrDefault(c => c.ClientId == "roleradar-web");

        Assert.NotNull(webClient);
        Assert.True(webClient.RequirePkce, "Web client must require PKCE (RFC 7636).");
        Assert.True(webClient.RequireClientSecret, "Confidential web client must require secret.");
        Assert.Contains(GrantType.AuthorizationCode, webClient.AllowedGrantTypes);

        // Strict lifetimes per ADR-0017
        Assert.Equal(600, webClient.AccessTokenLifetime);             // 10 minutes
        Assert.Equal(60, webClient.AuthorizationCodeLifetime);        // 60 seconds
        Assert.Equal(2592000, webClient.AbsoluteRefreshTokenLifetime); // 30 days
        Assert.Equal(1296000, webClient.SlidingRefreshTokenLifetime);  // 15 days
        Assert.Equal(TokenUsage.OneTimeOnly, webClient.RefreshTokenUsage);
        Assert.Equal(TokenExpiration.Sliding, webClient.RefreshTokenExpiration);

        Assert.Contains("openid", webClient.AllowedScopes);
        Assert.Contains("profile", webClient.AllowedScopes);
        Assert.Contains("roleradar.api", webClient.AllowedScopes);
        Assert.Contains("offline_access", webClient.AllowedScopes);
    }

    [Fact]
    public void HeadlessClient_Has5MinuteLifetimeAndNarrowScopes()
    {
        var clients = IdentityServerConfig.GetClients(_configuration).ToArray();
        var headlessClient = clients.FirstOrDefault(c => c.ClientId == "roleradar-headless");

        Assert.NotNull(headlessClient);
        Assert.Contains(GrantType.ClientCredentials, headlessClient.AllowedGrantTypes);
        Assert.Equal(300, headlessClient.AccessTokenLifetime); // 5 minutes

        Assert.Single(headlessClient.AllowedScopes);
        Assert.Contains("roleradar.api.read", headlessClient.AllowedScopes);
        Assert.Equal(string.Empty, headlessClient.ClientClaimsPrefix);
        Assert.Contains(headlessClient.Claims, c => c.Type == "sub" && c.Value == "roleradar-headless");
    }

    [Fact]
    public void HeadlessClient_UsesConfiguredSubjectClaim_WhenProvided()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Clients:Web:Secret"] = "web-client-test-secret-123456789012345",
                ["Clients:Headless:Secret"] = "headless-test-secret-123456789012345",
                ["Clients:Headless:Subject"] = "custom-headless-automation-bot"
            })
            .Build();

        var headlessClient = IdentityServerConfig.GetClients(config).Single(c => c.ClientId == "roleradar-headless");
        Assert.Contains(headlessClient.Claims, c => c.Type == "sub" && c.Value == "custom-headless-automation-bot");
    }

    [Theory]
    [InlineData(null, "valid-headless-secret-1234567890")]
    [InlineData("   ", "valid-headless-secret-1234567890")]
    [InlineData("valid-web-secret-1234567890", null)]
    [InlineData("valid-web-secret-1234567890", "")]
    public void GetClients_WhenSecretsMissingOrEmpty_FailsClosedAndWritesStderrWithoutLeakingSecrets(
        string? webSecret,
        string? headlessSecret)
    {
        var prevWebEnv = Environment.GetEnvironmentVariable("ROLERADAR_WEB_CLIENT_SECRET");
        var prevHeadlessEnv = Environment.GetEnvironmentVariable("ROLERADAR_HEADLESS_CLIENT_SECRET");
        var prevErr = Console.Error;
        using var errWriter = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable("ROLERADAR_WEB_CLIENT_SECRET", null);
            Environment.SetEnvironmentVariable("ROLERADAR_HEADLESS_CLIENT_SECRET", null);
            Console.SetError(errWriter);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Clients:Web:Secret"] = webSecret,
                    ["Clients:Headless:Secret"] = headlessSecret
                })
                .Build();

            var ex = Assert.Throws<InvalidOperationException>(() => IdentityServerConfig.GetClients(config).ToArray());
            var stderr = errWriter.ToString();

            Assert.Contains("RoleRadar.Identity refused to start", ex.Message, StringComparison.Ordinal);
            Assert.Contains("RoleRadar.Identity refused to start", stderr, StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(webSecret))
            {
                Assert.DoesNotContain(webSecret, stderr, StringComparison.Ordinal);
            }
            if (!string.IsNullOrWhiteSpace(headlessSecret))
            {
                Assert.DoesNotContain(headlessSecret, stderr, StringComparison.Ordinal);
            }
        }
        finally
        {
            Console.SetError(prevErr);
            Environment.SetEnvironmentVariable("ROLERADAR_WEB_CLIENT_SECRET", prevWebEnv);
            Environment.SetEnvironmentVariable("ROLERADAR_HEADLESS_CLIENT_SECRET", prevHeadlessEnv);
        }
    }

    [Fact]
    public void ApiScopes_AreConfiguredCorrectly()
    {
        var scopes = IdentityServerConfig.GetApiScopes().ToArray();

        Assert.Contains(scopes, s => s.Name == "roleradar.api");
        Assert.Contains(scopes, s => s.Name == "roleradar.api.read");
    }
}
