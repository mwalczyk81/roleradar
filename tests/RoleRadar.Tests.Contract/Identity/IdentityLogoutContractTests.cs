using System.Net;
using Duende.IdentityServer;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

public sealed class IdentityLogoutContractTests
{
    [Fact]
    public async Task EndSession_SignsOutDuendeAuthenticationSchemes_AndSubsequentAuthorizeRedirectsToLogin()
    {
        var (app, client, cookies, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            var idToken = await IdentityContractTestServer.LoginAndGetIdTokenAsync(client, app.Services);

            var endSessionState = "post-logout-state-schemes";
            var endSessionUrl = $"/connect/endsession?id_token_hint={Uri.EscapeDataString(idToken)}&post_logout_redirect_uri={Uri.EscapeDataString("https://localhost:5003/auth/logout")}&state={endSessionState}";

            var endSessionResponse = await client.GetAsync(endSessionUrl);
            IdentityContractTestServer.AssertIsRedirect(endSessionResponse);

            var logoutLocation = endSessionResponse.Headers.Location!.ToString();
            var logoutResponse = await client.GetAsync(logoutLocation);
            Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);

            // Both IdentityServer default cookie and signout cookie schemes must be signed out
            var setCookieHeaders = logoutResponse.Headers.TryGetValues("Set-Cookie", out var sc)
                ? string.Join(";", sc)
                : string.Empty;

            Assert.Contains(IdentityServerConstants.DefaultCookieAuthenticationScheme + "=", setCookieHeaders, StringComparison.OrdinalIgnoreCase);

            // Subsequent interactive /connect/authorize must cleanly redirect to /Account/Login rather than failing
            var rechallengeVerifier = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~0123456789";
            var rechallengeChallenge = Microsoft.AspNetCore.WebUtilities.Base64UrlTextEncoder.Encode(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(rechallengeVerifier)));
            var authorizeUrl = $"/connect/authorize?client_id=roleradar-web&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString("https://localhost:5003/auth/callback")}&code_challenge={rechallengeChallenge}&code_challenge_method=S256&state=check-rechallenge";

            var authorizeResponse = await client.GetAsync(authorizeUrl);
            IdentityContractTestServer.AssertIsRedirect(authorizeResponse);
            var authorizeLocation = authorizeResponse.Headers.Location!.ToString();
            Assert.Contains("/Account/Login", authorizeLocation, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(dbDir, true); } catch { }
        }
    }

    [Fact]
    public async Task EndSession_WithValidIdTokenHint_LogsOutAndRedirectsToRegisteredPostLogoutUri()
    {
        var (app, client, cookies, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            var idToken = await IdentityContractTestServer.LoginAndGetIdTokenAsync(client, app.Services);

            var cookiesBefore = cookies.GetCookies(new Uri(client.BaseAddress!.ToString()));
            Assert.Contains(cookiesBefore.Cast<Cookie>(), c => c.Name.Contains("Identity.Application"));

            var endSessionState = "post-logout-state-987";
            var endSessionUrl = $"/connect/endsession?id_token_hint={Uri.EscapeDataString(idToken)}&post_logout_redirect_uri={Uri.EscapeDataString("https://localhost:5003/auth/logout")}&state={endSessionState}";

            var endSessionResponse = await client.GetAsync(endSessionUrl);
            IdentityContractTestServer.AssertIsRedirect(endSessionResponse);

            var logoutLocation = endSessionResponse.Headers.Location!.ToString();
            Assert.Contains("/Account/Logout", logoutLocation, StringComparison.OrdinalIgnoreCase);

            var logoutResponse = await client.GetAsync(logoutLocation);
            Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
            Assert.Equal($"https://localhost:5003/auth/logout?state={endSessionState}", logoutResponse.Headers.Location!.ToString());

            var cookiesAfter = cookies.GetCookies(new Uri(client.BaseAddress!.ToString()));
            var appCookie = cookiesAfter.Cast<Cookie>().FirstOrDefault(c => c.Name.Contains("Identity.Application"));
            Assert.True(appCookie == null || appCookie.Expired || string.IsNullOrEmpty(appCookie.Value));

            var promptNoneVerifier = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~0123456789";
            var promptNoneChallenge = Microsoft.AspNetCore.WebUtilities.Base64UrlTextEncoder.Encode(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(promptNoneVerifier)));
            var promptNoneUrl = $"/connect/authorize?client_id=roleradar-web&response_type=code&scope=openid&redirect_uri={Uri.EscapeDataString("https://localhost:5003/auth/callback")}&code_challenge={promptNoneChallenge}&code_challenge_method=S256&prompt=none&state=check-prompt-none";
            var promptNoneResponse = await client.GetAsync(promptNoneUrl);
            IdentityContractTestServer.AssertIsRedirect(promptNoneResponse);
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
        var (app, client, cookies, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            var idToken = await IdentityContractTestServer.LoginAndGetIdTokenAsync(client, app.Services);

            var unregisteredUri = "https://attacker.example.com/steal";
            var endSessionUrl = $"/connect/endsession?id_token_hint={Uri.EscapeDataString(idToken)}&post_logout_redirect_uri={Uri.EscapeDataString(unregisteredUri)}&state=xyz";

            var endSessionResponse = await client.GetAsync(endSessionUrl);
            IdentityContractTestServer.AssertIsRedirect(endSessionResponse);

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

    [Fact]
    public async Task Logout_GetWithoutLogoutId_LeavesIdentityCookieIntact()
    {
        var (app, client, cookies, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            await IdentityContractTestServer.LoginAndGetIdTokenAsync(client, app.Services);

            var cookiesBefore = cookies.GetCookies(new Uri(client.BaseAddress!.ToString()));
            var appCookieBefore = cookiesBefore.Cast<Cookie>().FirstOrDefault(c => c.Name.Contains("Identity.Application"));
            Assert.NotNull(appCookieBefore);
            Assert.False(string.IsNullOrEmpty(appCookieBefore.Value));

            // Directly GET /Account/Logout with no logoutId or validated logout context
            var logoutResponse = await client.GetAsync("/Account/Logout");
            Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

            // Verify Identity cookie is intact (not expired, value preserved)
            var cookiesAfter = cookies.GetCookies(new Uri(client.BaseAddress!.ToString()));
            var appCookieAfter = cookiesAfter.Cast<Cookie>().FirstOrDefault(c => c.Name.Contains("Identity.Application"));
            Assert.NotNull(appCookieAfter);
            Assert.False(appCookieAfter.Expired);
            Assert.Equal(appCookieBefore.Value, appCookieAfter.Value);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(dbDir, true); } catch { }
        }
    }

    [Fact]
    public async Task Logout_PostWithoutAntiforgeryToken_ReturnsBadRequestAndLeavesIdentityCookieIntact()
    {
        var (app, client, cookies, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            await IdentityContractTestServer.LoginAndGetIdTokenAsync(client, app.Services);

            var cookiesBefore = cookies.GetCookies(new Uri(client.BaseAddress!.ToString()));
            var appCookieBefore = cookiesBefore.Cast<Cookie>().FirstOrDefault(c => c.Name.Contains("Identity.Application"));
            Assert.NotNull(appCookieBefore);
            Assert.False(string.IsNullOrEmpty(appCookieBefore.Value));

            // Directly POST /Account/Logout with empty form content (no antiforgery token)
            var postContent = new FormUrlEncodedContent(new Dictionary<string, string>());
            var logoutResponse = await client.PostAsync("/Account/Logout", postContent);
            Assert.Equal(HttpStatusCode.BadRequest, logoutResponse.StatusCode);

            // Verify Identity cookie is intact (not expired, value preserved)
            var cookiesAfter = cookies.GetCookies(new Uri(client.BaseAddress!.ToString()));
            var appCookieAfter = cookiesAfter.Cast<Cookie>().FirstOrDefault(c => c.Name.Contains("Identity.Application"));
            Assert.NotNull(appCookieAfter);
            Assert.False(appCookieAfter.Expired);
            Assert.Equal(appCookieBefore.Value, appCookieAfter.Value);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(dbDir, true); } catch { }
        }
    }
}
