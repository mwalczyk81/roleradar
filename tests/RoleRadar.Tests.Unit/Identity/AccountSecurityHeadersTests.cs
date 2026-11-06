using Duende.IdentityServer.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Identity.Configuration;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

/// <summary>
/// 009 US4: the /Account pages carry a CSP whose form-action lists the origins of every registered client's redirect and
/// post-logout URIs (the sign-in and sign-out posts end at the web app, and Chromium enforces form-action on that chain).
/// The origins are derived from the clients, never hard-coded. Fictional origins only.
/// </summary>
public sealed class AccountSecurityHeadersTests
{
    private const string BasePolicy =
        "default-src 'none'; style-src 'self'; font-src 'self'; script-src 'self'; img-src 'self' data:; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; form-action 'self'";

    private static List<Client> ClientsFor(string redirect, string postLogout) =>
        IdentityServerConfig.GetClients(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Clients:Web:Secret"] = "web-secret-test-123456789012345",
            ["Clients:Headless:Secret"] = "headless-secret-test-123456789012345",
            ["Clients:Web:RedirectUri"] = redirect,
            ["Clients:Web:PostLogoutRedirectUri"] = postLogout,
        }).Build()).ToList();

    private static async Task<IHeaderDictionary> RunAsync(IEnumerable<Client> clients, string path)
    {
        var app = new ApplicationBuilder(new ServiceCollection().BuildServiceProvider());
        app.UseAccountSecurityHeaders(clients);
        app.Run(_ => Task.CompletedTask);
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        await app.Build()(context);
        return context.Response.Headers;
    }

    [Fact]
    public async Task AccountPaths_GetThePolicyWithTheRedirectOriginsOfEveryClient()
    {
        var clients = ClientsFor("https://app.example.test:8443/auth/callback", "https://app.example.test:8443/auth/logout");

        foreach (var path in new[] { "/Account/Login", "/Account/Logout", "/account/login" })
        {
            var headers = await RunAsync(clients, path);

            Assert.Equal($"{BasePolicy} https://app.example.test:8443", headers.ContentSecurityPolicy.ToString());
            Assert.Equal("nosniff", headers.XContentTypeOptions.ToString());
            Assert.Equal("no-referrer", headers["Referrer-Policy"].ToString());
        }
    }

    [Fact]
    public async Task FormAction_ListsEachDistinctOrigin_FromRedirectAndPostLogoutUris()
    {
        var clients = ClientsFor("https://one.example.test/auth/callback", "https://two.example.test:9443/auth/logout");

        var headers = await RunAsync(clients, "/Account/Login");

        Assert.Equal($"{BasePolicy} https://one.example.test https://two.example.test:9443", headers.ContentSecurityPolicy.ToString());
    }

    [Fact]
    public void BuildPolicy_OmitsDefaultPorts_AndNeverRepeatsAnOrigin()
    {
        var clients = ClientsFor("https://app.example.test:443/auth/callback", "https://app.example.test/auth/logout");

        Assert.Equal($"{BasePolicy} https://app.example.test", AccountSecurityHeaders.BuildContentSecurityPolicy(clients));
    }

    [Fact]
    public void BuildPolicy_WithNoRedirectUris_KeepsFormActionToSelf()
    {
        Assert.Equal(BasePolicy, AccountSecurityHeaders.BuildContentSecurityPolicy([new Client { ClientId = "headless-only" }]));
    }

    [Theory]
    [InlineData("/connect/token")]
    [InlineData("/connect/authorize")]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/health")]
    [InlineData("/AccountSettings")]
    public async Task OtherPaths_GetNoneOfTheseHeaders(string path)
    {
        var headers = await RunAsync(ClientsFor("https://app.example.test/auth/callback", "https://app.example.test/auth/logout"), path);

        Assert.False(headers.ContainsKey("Content-Security-Policy"));
        Assert.False(headers.ContainsKey("X-Content-Type-Options"));
        Assert.False(headers.ContainsKey("Referrer-Policy"));
    }
}
