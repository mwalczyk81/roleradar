using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoleRadar.Identity.Data;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

/// <summary>
/// Pre-scrub census contract tests for Rules 1, 2, and 3.
/// These tests capture log records before any formatter or LogScrubber runs,
/// demonstrating whether real producers exist at the repo's default logging levels.
/// </summary>
public sealed partial class PreScrubCensusContractTests
{
    private const string SentinelPassword = "SENTINEL-PW-981a"; // gitleaks:allow (fictional redaction test vector, not a credential)
    private const string SentinelUsername = "SENTINEL-USER-981a";
    private const string SentinelBearerToken = "SENTINEL-BEARER-TOKEN-842b";
    private const string SentinelBasicToken = "SENTINEL-BASIC-TOKEN-731c";
    private const string SentinelMongoUser = "fictional-user";
    private const string SentinelMongoPass = "fictional-pass-621d";

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*\b")]
    private static partial Regex JwtPattern();

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
    public async Task SignInFlows_AtDefaultLevels_EmitNoBearerBasicOrJwt_PreScrub()
    {
        var capture = new PreScrubLogCapture();
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(
            builder => builder.Logging.AddProvider(capture));

        try
        {
            // 1. Wrong password and unknown user
            var wrongPassword = await PostPasswordAsync(client, "testowner", SentinelPassword);
            var unknownUser = await PostPasswordAsync(client, SentinelUsername, SentinelPassword);
            Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
            Assert.Equal(HttpStatusCode.OK, unknownUser.StatusCode);

            // 2. Full successful login that issues an ID token (JWT)
            var idToken = await IdentityContractTestServer.LoginAndGetIdTokenAsync(client, app.Services);
            Assert.NotEmpty(idToken);
            Assert.Matches(JwtPattern(), idToken);

            // Assert on PRE-SCRUB capture:
            // Non-vacuous: entries exist
            Assert.NotEmpty(capture.Entries);

            // Rule 1: No log entry contains "Bearer " or "Basic " credentials
            Assert.DoesNotContain(capture.Entries, e =>
                e.Message.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("Basic ", StringComparison.OrdinalIgnoreCase));

            // Rule 2: No log entry contains the issued JWT or any string matching the JWT regex
            Assert.DoesNotContain(capture.Entries, e =>
                e.Message.Contains(idToken, StringComparison.Ordinal)
                || JwtPattern().IsMatch(e.Message));

            // Sentinel user & password also never reach pre-scrub logs
            Assert.DoesNotContain(capture.Entries, e =>
                e.Message.Contains(SentinelPassword, StringComparison.Ordinal)
                || e.Message.Contains(SentinelUsername, StringComparison.Ordinal));
        }
        finally
        {
            client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            try { Directory.Delete(dbDir, true); } catch { }
        }
    }

    [Fact]
    public async Task OutboundHttpClient_FailedCall_EmitsNoBearerOrBasicCredentials_PreScrub()
    {
        // Set up host with DefaultLevels (System=Warning) and capture pre-scrub logs
        var capture = new PreScrubLogCapture();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddRoleRadarLogging("test-client", LogStream.Split);
            logging.AddProvider(capture);
        });
        services.AddHttpClient("test-outbound");

        using var sp = services.BuildServiceProvider();
        var factory = sp.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("test-outbound");

        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:1/fictional-endpoint");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SentinelBearerToken);
        request.Headers.Add("X-Basic-Auth", $"Basic {SentinelBasicToken}");

        // Attempt request to unreachable port 1 -> throws HttpRequestException
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.SendAsync(request));

        // Assert on PRE-SCRUB capture:
        // System.Net.Http at default Warning level emits no headers or trace messages
        Assert.DoesNotContain(capture.Entries, e =>
            e.Message.Contains(SentinelBearerToken, StringComparison.Ordinal)
            || e.Message.Contains(SentinelBasicToken, StringComparison.Ordinal)
            || e.Message.Contains("Bearer ", StringComparison.OrdinalIgnoreCase)
            || e.Message.Contains("Basic ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ApiHost_FailedMongoConnection_EmitsNoMongoCredentials_PreScrub()
    {
        var capture = new PreScrubLogCapture();
        var unreachConnString = $"mongodb://{SentinelMongoUser}:{SentinelMongoPass}@127.0.0.1:1/nonexistent?directConnection=true&serverSelectionTimeoutMS=200";

        var app = RoleRadar.Api.Program.CreateApp(
            ["--urls", "http://127.0.0.1:0"],
            builder =>
            {
                builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:MongoConnection"] = unreachConnString,
                    ["DatabaseName"] = "rrtest_census_mongo",
                    ["Authentication:Authority"] = "https://identity.fictional.invalid",
                    ["Authentication:Audience"] = "roleradar-api",
                    ["Owner:AllowedSubjects:0"] = "testowner"
                });
                builder.Logging.AddProvider(capture);
            });

        await app.StartAsync();
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:0") };
            var server = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>();
            var address = server.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses.Single();
            client.BaseAddress = new Uri(address);

            // Health check checks Mongo readiness, which will fail to reach 127.0.0.1:1
            var response = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

            // Assert on PRE-SCRUB capture:
            Assert.NotEmpty(capture.Entries);

            // Neither the password nor user:pass@ is emitted in any pre-scrub log record
            Assert.DoesNotContain(capture.Entries, e =>
                e.Message.Contains(SentinelMongoPass, StringComparison.Ordinal)
                || e.Message.Contains($"{SentinelMongoUser}:{SentinelMongoPass}@", StringComparison.Ordinal));
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }
}
