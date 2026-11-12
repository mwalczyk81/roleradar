using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoleRadar.Identity.Data;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

/// <summary>
/// Feature 013 US1 (FR-012, FR-013, FR-018): no password, typed username, TOTP secret, client secret or issued token
/// reaches the identity service's log output, through wrong-password, unknown-user and full sign-in plus token flows,
/// including Duende's own request and event logging. Sentinel values are fictional.
/// </summary>
public sealed partial class IdentityRedactionContractTests
{
    private const string SentinelPassword = "SENTINEL-PW-7f3a"; // gitleaks:allow (fictional redaction test vector, not a credential)
    private const string SentinelUsername = "SENTINEL-USER-7f3a";
    private const string TestOwnerPassword = "Password123!";
    private const string WebClientSecret = "web-secret-test-123456789012345";

    [GeneratedRegex(@"name=""__RequestVerificationToken""[^>]*?value=""([^""]+)""")]
    private static partial Regex AntiForgery();

    private static async Task<HttpResponseMessage> PostPasswordAsync(HttpClient client, string username, string password)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = AntiForgery().Match(html).Groups[1].Value;
        return await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["ReturnUrl"] = "/",
            ["RequiresTwoFactor"] = "false",
            ["Input.Username"] = username,
            ["Input.Password"] = password,
        }));
    }

    [Fact]
    public async Task SignInFlows_NeverLogSecretsOrTheTypedUsername()
    {
        var capture = new FormatterLogCapture(RoleRadarConsoleFormatter.Create("identity", LogFormat.Json));
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(builder => builder.Logging.AddProvider(capture));
        try
        {
            var wrongPassword = await PostPasswordAsync(client, "testowner", SentinelPassword);
            var unknownUser = await PostPasswordAsync(client, SentinelUsername, SentinelPassword);
            Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
            Assert.Equal(HttpStatusCode.OK, unknownUser.StatusCode);

            var idToken = await IdentityContractTestServer.LoginAndGetIdTokenAsync(client, app.Services);

            string authenticatorKey;
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<IdentityAppDbContext>();
                var user = db.Users.Single(u => u.UserName == "testowner");
                authenticatorKey = db.UserTokens.Single(t => t.UserId == user.Id && t.Name == "AuthenticatorKey").Value!;
            }

            // Non-vacuous capture first: lines exist and the login request's RequestResponseLog record is among them.
            Assert.NotEmpty(capture.Lines);
            Assert.Contains(capture.Lines, line =>
                line.Contains("\"event\":\"RequestResponseLog\"", StringComparison.Ordinal)
                && line.Contains("\"Path\":\"/Account/Login\"", StringComparison.Ordinal));

            var text = capture.Text;
            foreach (var secret in new[] { SentinelPassword, SentinelUsername, TestOwnerPassword, WebClientSecret, idToken, authenticatorKey })
            {
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            }
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            try
            {
                Directory.Delete(dbDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
