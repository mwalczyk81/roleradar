using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

public sealed class IdentityLogoutContractTests
{
    private static async Task<(WebApplication App, HttpClient Client, CookieContainer Cookies, string DbDir)> StartHostAsync()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "rrtest_identity_logout_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "identity.db");

        var app = RoleRadar.Identity.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:IdentityConnection"] = $"Data Source={dbPath}",
                    ["Clients:Web:Secret"] = "web-secret-test-123456789012345",
                    ["Clients:Headless:Secret"] = "headless-secret-test-123456789012345",
                    ["Clients:Web:RedirectUri"] = "https://localhost:5003/auth/callback",
                    ["Clients:Web:PostLogoutRedirectUri"] = "https://localhost:5003/auth/logout",
                });
            });

        await app.StartAsync();

        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
            var provisioningService = new OwnerProvisioningService(dbContext);
            var provisionResult = await provisioningService.ProvisionOwnerAsync("testowner", "Password123!");
            var totpCode = provisioningService.ComputeTotpCode(provisionResult.AuthenticatorKey);
            var activated = await provisioningService.VerifyTotpAndActivateAsync(provisionResult.UserId, totpCode);
            Assert.True(activated);
        }

        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        var cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = false
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri(address) };

        return (app, client, cookies, tempDir);
    }

    private static void AssertIsRedirect(HttpResponseMessage response)
    {
        Assert.True(
            response.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.MovedPermanently or HttpStatusCode.TemporaryRedirect,
            $"Expected redirect status, got {response.StatusCode}");
    }

    private static async Task<string> LoginAndGetIdTokenAsync(
        HttpClient client,
        IServiceProvider services)
    {
        var codeVerifier = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~0123456789";
        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        var codeChallenge = Base64UrlTextEncoder.Encode(challengeBytes);
        var state = "state-" + Guid.NewGuid().ToString("N");

        var authUrl = $"/connect/authorize?client_id=roleradar-web&response_type=code&scope=openid%20profile%20roleradar.api%20offline_access&redirect_uri={Uri.EscapeDataString("https://localhost:5003/auth/callback")}&code_challenge={codeChallenge}&code_challenge_method=S256&state={state}";
        var authResponse = await client.GetAsync(authUrl);
        AssertIsRedirect(authResponse);

        var loginUrl = authResponse.Headers.Location!.ToString();
        var loginPageResponse = await client.GetAsync(loginUrl);
        Assert.Equal(HttpStatusCode.OK, loginPageResponse.StatusCode);
        var loginHtml = await loginPageResponse.Content.ReadAsStringAsync();

        var tokenMatch = Regex.Match(loginHtml, @"name=""__RequestVerificationToken""[^>]*?value=""([^""]+)""");
        Assert.True(tokenMatch.Success, $"Expected __RequestVerificationToken. Status: {loginPageResponse.StatusCode}, Url: {loginUrl}, Html: {loginHtml}");
        var antiForgeryToken = tokenMatch.Groups[1].Value;

        var returnUrlMatch = Regex.Match(loginHtml, @"name=""ReturnUrl""[^>]*?value=""([^""]+)""");
        var returnUrl = returnUrlMatch.Success ? WebUtility.HtmlDecode(returnUrlMatch.Groups[1].Value) : authUrl;

        var step1Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken,
            ["ReturnUrl"] = returnUrl,
            ["RequiresTwoFactor"] = "false",
            ["Input.Username"] = "testowner",
            ["Input.Password"] = "Password123!"
        });

        var step1Response = await client.PostAsync(loginUrl, step1Content);
        Assert.Equal(HttpStatusCode.OK, step1Response.StatusCode);
        var step1Html = await step1Response.Content.ReadAsStringAsync();

        var token2Match = Regex.Match(step1Html, @"name=""__RequestVerificationToken""[^>]*?value=""([^""]+)""");
        Assert.True(token2Match.Success, "Expected __RequestVerificationToken in 2FA form");
        var antiForgeryToken2 = token2Match.Groups[1].Value;

        string totpCode;
        using (var scope = services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
            var user = dbContext.Users.Single(u => u.UserName == "testowner");
            var token = dbContext.UserTokens.Single(t => t.UserId == user.Id && t.Name == "AuthenticatorKey");
            var provisioningService = new OwnerProvisioningService(dbContext);
            totpCode = provisioningService.ComputeTotpCode(token.Value!);
        }

        var step2Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = antiForgeryToken2,
            ["ReturnUrl"] = returnUrl,
            ["RequiresTwoFactor"] = "true",
            ["TwoFactorInput.Code"] = totpCode
        });

        var step2Response = await client.PostAsync(loginUrl, step2Content);
        AssertIsRedirect(step2Response);
        var authorizeRedirectUrl = step2Response.Headers.Location!.ToString();

        var authorizeResultResponse = await client.GetAsync(authorizeRedirectUrl);
        AssertIsRedirect(authorizeResultResponse);
        var callbackUri = authorizeResultResponse.Headers.Location!;
        Assert.StartsWith("https://localhost:5003/auth/callback", callbackUri.ToString(), StringComparison.OrdinalIgnoreCase);

        var query = QueryHelpers.ParseQuery(callbackUri.Query);
        var code = query["code"].ToString();
        Assert.False(string.IsNullOrWhiteSpace(code), "Expected authorization code");

        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "/connect/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = "roleradar-web",
                ["client_secret"] = "web-secret-test-123456789012345",
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = "https://localhost:5003/auth/callback",
                ["code_verifier"] = codeVerifier
            })
        };

        var tokenResponse = await client.SendAsync(tokenRequest);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(tokenJson);
        var idToken = doc.RootElement.GetProperty("id_token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(idToken), "Expected valid id_token");

        return idToken!;
    }

    [Fact]
    public async Task EndSession_WithValidIdTokenHint_LogsOutAndRedirectsToRegisteredPostLogoutUri()
    {
        var (app, client, cookies, dbDir) = await StartHostAsync();
        try
        {
            var idToken = await LoginAndGetIdTokenAsync(client, app.Services);

            var cookiesBefore = cookies.GetCookies(new Uri(client.BaseAddress!.ToString()));
            Assert.Contains(cookiesBefore.Cast<Cookie>(), c => c.Name.Contains("Identity.Application"));

            var endSessionState = "post-logout-state-987";
            var endSessionUrl = $"/connect/endsession?id_token_hint={Uri.EscapeDataString(idToken)}&post_logout_redirect_uri={Uri.EscapeDataString("https://localhost:5003/auth/logout")}&state={endSessionState}";

            var endSessionResponse = await client.GetAsync(endSessionUrl);
            AssertIsRedirect(endSessionResponse);

            var logoutLocation = endSessionResponse.Headers.Location!.ToString();
            Assert.Contains("/Account/Logout", logoutLocation, StringComparison.OrdinalIgnoreCase);

            var logoutResponse = await client.GetAsync(logoutLocation);
            Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
            Assert.Equal($"https://localhost:5003/auth/logout?state={endSessionState}", logoutResponse.Headers.Location!.ToString());

            var cookiesAfter = cookies.GetCookies(new Uri(client.BaseAddress!.ToString()));
            var appCookie = cookiesAfter.Cast<Cookie>().FirstOrDefault(c => c.Name.Contains("Identity.Application"));
            Assert.True(appCookie == null || appCookie.Expired || string.IsNullOrEmpty(appCookie.Value));

            var promptNoneVerifier = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~0123456789";
            var promptNoneChallenge = Base64UrlTextEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(promptNoneVerifier)));
            var promptNoneUrl = $"/connect/authorize?client_id=roleradar-web&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString("https://localhost:5003/auth/callback")}&code_challenge={promptNoneChallenge}&code_challenge_method=S256&prompt=none&state=check-prompt-none";
            var promptNoneResponse = await client.GetAsync(promptNoneUrl);
            AssertIsRedirect(promptNoneResponse);
            var promptNoneLocation = promptNoneResponse.Headers.Location!.ToString();
            Assert.Contains("error=login_required", promptNoneLocation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(dbDir, true); } catch { }
        }
    }

    [Fact]
    public async Task EndSession_WithUnregisteredPostLogoutRedirectUri_DoesNotRedirectToUnregisteredUri()
    {
        var (app, client, cookies, dbDir) = await StartHostAsync();
        try
        {
            var idToken = await LoginAndGetIdTokenAsync(client, app.Services);

            var unregisteredUri = "https://attacker.example.com/steal";
            var endSessionUrl = $"/connect/endsession?id_token_hint={Uri.EscapeDataString(idToken)}&post_logout_redirect_uri={Uri.EscapeDataString(unregisteredUri)}&state=xyz";

            var endSessionResponse = await client.GetAsync(endSessionUrl);
            AssertIsRedirect(endSessionResponse);

            var logoutLocation = endSessionResponse.Headers.Location!.ToString();
            var logoutResponse = await client.GetAsync(logoutLocation);

            if (logoutResponse.StatusCode == HttpStatusCode.Redirect)
            {
                Assert.NotEqual(unregisteredUri, logoutResponse.Headers.Location!.ToString());
                Assert.DoesNotContain("attacker.example.com", logoutResponse.Headers.Location!.ToString());
            }
            else
            {
                Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
            }
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(dbDir, true); } catch { }
        }
    }
}
