using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RoleRadar.Cli.Commands.Identity;
using RoleRadar.Identity.Data;
using RoleRadar.Identity.Services;
using Xunit;

namespace RoleRadar.Tests.Unit.Identity;

/// <summary>
/// The API authorizes by an exact, case-sensitive (<see cref="StringComparison.Ordinal"/>) match of the token's
/// <c>sub</c> claim against <c>Owner:AllowedSubjects</c>. An operator therefore has to paste the exact string
/// Identity emits. These tests prove that the value <c>identity create-owner</c> prints is that string, by
/// issuing a real access token for the provisioned user and comparing.
/// </summary>
public sealed class CreateOwnerSubjectTests
{
    private const string WebSecret = "CreateOwnerWebSecret!2026";
    private const string RedirectUri = "https://localhost:5003/signin-oidc";
    private const string Username = "subject-owner";
    private const string Password = "StrongPass!2026#Subject";

    [Fact]
    public async Task CreateOwnerOutput_PrintsSubjectEqualToSubClaimOfIssuedAccessToken_InLowercaseDFormat()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"roleradar-subject-{Guid.NewGuid():N}.db");
        var keyDir = Path.Combine(Path.GetTempPath(), $"roleradar-subject-keys-{Guid.NewGuid():N}");

        try
        {
            await using var identityApp = RoleRadar.Identity.Program.CreateApp(
                ["--urls", "http://127.0.0.1:0"],
                builder =>
                {
                    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:IdentityConnection"] = $"Data Source={dbPath}",
                        ["Clients:Web:Secret"] = WebSecret,
                        ["Clients:Headless:Secret"] = "CreateOwnerHeadlessSecret!2026",
                        ["Clients:Web:RedirectUri"] = RedirectUri
                    });
                    builder.Services.PostConfigure<Duende.IdentityServer.Configuration.IdentityServerOptions>(o =>
                    {
                        o.KeyManagement.KeyPath = keyDir;
                    });
                });
            await identityApp.StartAsync();

            var baseUrl = identityApp.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/');

            // Provision exactly like `identity create-owner`, then render the summary through the command's own writer.
            OwnerProvisioningResult provisioned;
            string totpSecret;
            using (var scope = identityApp.Services.CreateScope())
            {
                var provisioning = new OwnerProvisioningService(scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>());
                provisioned = await provisioning.ProvisionOwnerAsync(Username, Password);
                totpSecret = provisioned.AuthenticatorKey;
                Assert.True(await provisioning.VerifyTotpAndActivateAsync(provisioned.UserId, provisioning.ComputeTotpCode(totpSecret)));
            }

            using var output = new StringWriter();
            CreateOwnerCommand.WriteOwnerSummary(output, provisioned);
            var printedSubject = ExtractPrintedSubject(output.ToString());

            var tokenSubject = await IssueAccessTokenSubjectAsync(identityApp, baseUrl, totpSecret);

            // Exact, case-sensitive equality: the same comparison the API allowlist applies.
            Assert.Equal(tokenSubject, printedSubject, StringComparer.Ordinal);
            Assert.True(
                Guid.TryParseExact(printedSubject, "D", out var parsed) && parsed == provisioned.UserId,
                $"Printed subject '{printedSubject}' must be the user id in 'D' format.");
            Assert.Equal(printedSubject.ToLowerInvariant(), printedSubject);

            await identityApp.StopAsync();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            TryDelete(dbPath);
            if (Directory.Exists(keyDir))
            {
                try { Directory.Delete(keyDir, recursive: true); } catch (IOException) { }
            }
        }
    }

    [Fact]
    public void WriteOwnerSummary_LabelsSubjectForAllowedSubjectEnvVar_AndDoesNotAlterCase()
    {
        // Uses an id whose hex digits would differ under upper/N/B/P formats so any formatting drift is caught.
        var userId = Guid.Parse("0192E4A1-ABCD-7000-8000-00000000FACE");
        var result = new OwnerProvisioningResult(userId, "owner", "JBSWY3DPEHPK3PXP", "otpauth://totp/RoleRadar:owner?secret=JBSWY3DPEHPK3PXP&issuer=RoleRadar");

        using var output = new StringWriter();
        CreateOwnerCommand.WriteOwnerSummary(output, result);
        var text = output.ToString();

        Assert.Contains("ROLERADAR_OWNER_ALLOWED_SUBJECT", text, StringComparison.Ordinal);
        Assert.Equal("0192e4a1-abcd-7000-8000-00000000face", ExtractPrintedSubject(text));
    }

    private static string ExtractPrintedSubject(string text)
    {
        var match = Regex.Match(
            text,
            $"^{Regex.Escape(CreateOwnerCommand.OwnerSubjectLabel)}\\s+(?<sub>\\S+)\\s*$",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"Expected a line '{CreateOwnerCommand.OwnerSubjectLabel} <sub>' in:\n{text}");
        return match.Groups["sub"].Value;
    }

    private static async Task<string> IssueAccessTokenSubjectAsync(
        Microsoft.AspNetCore.Builder.WebApplication identityApp,
        string baseUrl,
        string totpSecret)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = true, CookieContainer = new CookieContainer() };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };

        var verifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk-0123456789abcdef";
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var authorize = $"/connect/authorize?client_id=roleradar-web&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&response_type=code&scope={Uri.EscapeDataString("openid profile roleradar.api")}" +
            $"&code_challenge={challenge}&code_challenge_method=S256&state=subject_state";

        var authResp = await client.GetAsync(authorize);
        var loginPathAndQuery = authResp.Headers.Location!.IsAbsoluteUri
            ? authResp.Headers.Location.PathAndQuery
            : authResp.Headers.Location.ToString();

        var loginHtml = await (await client.GetAsync(loginPathAndQuery)).Content.ReadAsStringAsync();
        var returnUrl = HttpUtility.ParseQueryString(new Uri(new Uri(baseUrl), loginPathAndQuery).Query)["ReturnUrl"]!;

        var step1 = await client.PostAsync(loginPathAndQuery, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = Username,
            ["Input.Password"] = Password,
            ["ReturnUrl"] = returnUrl,
            ["RequiresTwoFactor"] = "false",
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(loginHtml)
        }));
        var step2Html = await step1.Content.ReadAsStringAsync();

        string liveTotp;
        using (var scope = identityApp.Services.CreateScope())
        {
            liveTotp = new OwnerProvisioningService(scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>())
                .ComputeTotpCode(totpSecret);
        }

        var step2 = await client.PostAsync(loginPathAndQuery, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["TwoFactorInput.Code"] = liveTotp,
            ["ReturnUrl"] = returnUrl,
            ["RequiresTwoFactor"] = "true",
            ["__RequestVerificationToken"] = ExtractAntiForgeryToken(step2Html)
        }));
        Assert.True(step2.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod);

        var callback = step2.Headers.Location!.IsAbsoluteUri ? step2.Headers.Location.PathAndQuery : step2.Headers.Location.ToString();
        var callbackResp = await client.GetAsync(callback);
        var code = HttpUtility.ParseQueryString(callbackResp.Headers.Location!.Query)["code"];
        Assert.False(string.IsNullOrWhiteSpace(code));

        var tokenResp = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = "roleradar-web",
            ["client_secret"] = WebSecret,
            ["code"] = code!,
            ["redirect_uri"] = RedirectUri,
            ["code_verifier"] = verifier
        }));
        Assert.Equal(HttpStatusCode.OK, tokenResp.StatusCode);

        var payload = await tokenResp.Content.ReadFromJsonAsync<JsonElement>();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.GetProperty("access_token").GetString()!);
        return jwt.Claims.Single(c => c.Type == "sub").Value;
    }

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"(?<t>[^\"]+)\"");
        Assert.True(match.Success, "Anti-forgery token not found in login page HTML.");
        return match.Groups["t"].Value;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort temp cleanup.
        }
    }
}
