using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

/// <summary>
/// 009 US4: the rendered Login (password step, TOTP step, with and without validation errors) and Logout (signed out and
/// signed in) pages have no inline style or script, link the four kit and Identity assets with theme.js first, keep every
/// form field the sign-in flow depends on, and answer with the /Account CSP. Runs on the in-process Identity host with a
/// throwaway database and a fictional owner. That host cannot serve wwwroot (its content root is the test output folder),
/// so asset serving is checked on the real image in IdentityImageLoginPageTests.
/// </summary>
public sealed class IdentityAccountPagesContractTests
{
    private const string ExpectedCsp =
        "default-src 'none'; style-src 'self'; font-src 'self'; script-src 'self'; img-src 'self' data:; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; form-action 'self' https://localhost:5003";

    private static readonly string[] Assets = ["/js/theme.js", "/ds/tokens.css", "/ds/components/bundle.css", "/css/identity.css"];

    private static void AssertThemedPage(HttpResponseMessage response, string html, string heading)
    {
        AssertBrandAndHeading(html, heading);

        Assert.DoesNotContain("<style", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"\sstyle\s*=", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"<script(?![^>]*\ssrc=)", RegexOptions.IgnoreCase), html);
        Assert.DoesNotMatch(new Regex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase), html);

        var positions = Assets.Select(a => html.IndexOf($"\"{a}\"", StringComparison.Ordinal)).ToArray();
        for (var i = 0; i < Assets.Length; i++)
        {
            Assert.True(positions[i] >= 0, $"Expected the page to link {Assets[i]}.");
        }

        var firstStylesheet = html.IndexOf("<link", StringComparison.OrdinalIgnoreCase);
        Assert.True(positions[0] < firstStylesheet, "theme.js must come before the first stylesheet so the theme is set before paint.");

        Assert.Equal(ExpectedCsp, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
    }

    // The brand lockup is the web app's Brand component: the same four-element radar mark, decorative, plus the wordmark.
    private static void AssertBrandAndHeading(string html, string heading)
    {
        Assert.Contains("<svg class=\"brand-mark\" viewBox=\"0 0 24 24\" width=\"22\" height=\"22\" aria-hidden=\"true\" focusable=\"false\">", html, StringComparison.Ordinal);
        Assert.Contains("<circle cx=\"12\" cy=\"12\" r=\"9.5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"", html, StringComparison.Ordinal);
        Assert.Contains("<circle cx=\"12\" cy=\"12\" r=\"5\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.5\"", html, StringComparison.Ordinal);
        Assert.Contains("<line x1=\"12\" y1=\"12\" x2=\"18.5\" y2=\"5.5\" stroke=\"currentColor\" stroke-width=\"1.5\" stroke-linecap=\"round\"", html, StringComparison.Ordinal);
        Assert.Contains("<circle cx=\"18.5\" cy=\"5.5\" r=\"1.6\" fill=\"currentColor\"", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"brand-name\">RoleRadar</span>", html, StringComparison.Ordinal);

        Assert.Single(Regex.Matches(html, "<h1[ >]"));
        Assert.Matches(new Regex($@"<h1[^>]*>s*{Regex.Escape(heading)}s*</h1>"), html);
        Assert.DoesNotContain("RoleRadar Authentication", html, StringComparison.Ordinal);
    }

    private static string Token(string html)
    {
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""[^>]*?value=""([^""]+)""");
        Assert.True(match.Success, "Expected an anti-forgery token.");
        return match.Groups[1].Value;
    }

    private static FormUrlEncodedContent Form(string token, bool twoFactor, params (string Key, string Value)[] fields)
    {
        var values = new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/",
            ["RequiresTwoFactor"] = twoFactor ? "true" : "false",
        };
        foreach (var (key, value) in fields)
        {
            values[key] = value;
        }

        return new FormUrlEncodedContent(values);
    }

    [Fact]
    public async Task Login_PasswordStep_IsThemed_AndKeepsItsFields()
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            var response = await client.GetAsync("/Account/Login");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertThemedPage(response, html, "Sign in");
            foreach (var field in new[] { "name=\"Input.Username\"", "name=\"Input.Password\"", "name=\"ReturnUrl\"", "name=\"RequiresTwoFactor\"", "__RequestVerificationToken" })
            {
                Assert.Contains(field, html, StringComparison.Ordinal);
            }
        }
        finally
        {
            await Stop(app, dbDir);
        }
    }

    [Fact]
    public async Task Login_PasswordStep_WithValidationError_IsThemed_AndShowsTheError()
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            var token = Token(await client.GetStringAsync("/Account/Login"));
            var response = await client.PostAsync("/Account/Login", Form(token, false, ("Input.Username", "testowner"), ("Input.Password", "wrong-password-1!")));
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertThemedPage(response, html, "Sign in");
            Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
            Assert.Contains("<svg class=\"alert-icon\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"Input.Username\"", html, StringComparison.Ordinal);
        }
        finally
        {
            await Stop(app, dbDir);
        }
    }

    [Fact]
    public async Task Login_TotpStep_IsThemed_WithAndWithoutError_AndKeepsItsFields()
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            var token = Token(await client.GetStringAsync("/Account/Login"));
            var step1 = await client.PostAsync("/Account/Login", Form(token, false, ("Input.Username", "testowner"), ("Input.Password", "Password123!")));
            var step1Html = await step1.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, step1.StatusCode);
            AssertThemedPage(step1, step1Html, "Sign in");
            foreach (var field in new[] { "name=\"TwoFactorInput.Code\"", "name=\"ReturnUrl\"", "name=\"RequiresTwoFactor\"", "value=\"true\"", "__RequestVerificationToken" })
            {
                Assert.Contains(field, step1Html, StringComparison.Ordinal);
            }

            var step2 = await client.PostAsync("/Account/Login", Form(Token(step1Html), true, ("TwoFactorInput.Code", "000000")));
            var step2Html = await step2.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, step2.StatusCode);
            AssertThemedPage(step2, step2Html, "Sign in");
            Assert.Contains("Invalid verification code.", step2Html, StringComparison.Ordinal);
        }
        finally
        {
            await Stop(app, dbDir);
        }
    }

    [Fact]
    public async Task Logout_SignedOut_IsThemed_WithoutASignOutForm()
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            var response = await client.GetAsync("/Account/Logout");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertThemedPage(response, html, "You are signed out");
            Assert.Contains("/Account/Login", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await Stop(app, dbDir);
        }
    }

    [Fact]
    public async Task Logout_SignedIn_IsThemed_AndKeepsTheSignOutForm()
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            await IdentityContractTestServer.LoginAndGetIdTokenAsync(client, app.Services);

            var response = await client.GetAsync("/Account/Logout");
            var html = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertThemedPage(response, html, "You are signed out");
            Assert.Contains("<form method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sign Out", html, StringComparison.Ordinal);
        }
        finally
        {
            await Stop(app, dbDir);
        }
    }

    [Theory]
    [InlineData("/.well-known/openid-configuration")]
    [InlineData("/health")]
    public async Task NonAccountEndpoints_DoNotGetThePolicy(string path)
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("Content-Security-Policy"));
            Assert.False(response.Headers.Contains("Referrer-Policy"));
        }
        finally
        {
            await Stop(app, dbDir);
        }
    }

    private static async Task Stop(WebApplication app, string dbDir)
    {
        await app.StopAsync();
        await app.DisposeAsync();
        try { Directory.Delete(dbDir, true); } catch { }
    }
}
