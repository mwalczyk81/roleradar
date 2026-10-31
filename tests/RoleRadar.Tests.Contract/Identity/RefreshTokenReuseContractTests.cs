using Duende.IdentityServer.Models;
using Microsoft.Extensions.Configuration;
using RoleRadar.Identity.Configuration;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

public sealed class RefreshTokenReuseContractTests
{
    private readonly IConfiguration _configuration;

    public RefreshTokenReuseContractTests()
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
    public void RefreshTokens_AreConfiguredForOneTimeUse_WithRotationAndReuseDetection()
    {
        var clients = IdentityServerConfig.GetClients(_configuration).ToArray();
        var webClient = clients.Single(c => c.ClientId == "roleradar-web");

        // TokenUsage.OneTimeOnly ensures the token handle is invalidated upon use (rotating refresh token)
        Assert.Equal(TokenUsage.OneTimeOnly, webClient.RefreshTokenUsage);

        // TokenExpiration.Sliding ensures token expiration advances upon use up to AbsoluteRefreshTokenLifetime
        Assert.Equal(TokenExpiration.Sliding, webClient.RefreshTokenExpiration);

        // 30 days absolute cap, 15 days sliding window
        Assert.Equal(2592000, webClient.AbsoluteRefreshTokenLifetime);
        Assert.Equal(1296000, webClient.SlidingRefreshTokenLifetime);

        // Claims updated on refresh
        Assert.True(webClient.UpdateAccessTokenClaimsOnRefresh);
    }
}
