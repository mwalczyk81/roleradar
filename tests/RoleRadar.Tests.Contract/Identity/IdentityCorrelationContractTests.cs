using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

/// <summary>Feature 013 US1 (FR-004 to FR-006): the identity service accepts, generates and returns X-Correlation-ID.</summary>
public sealed class IdentityCorrelationContractTests
{
    private static async Task<(WebApplication App, HttpClient Client, string DbDir, FormatterLogCapture Capture)> StartAsync()
    {
        var capture = new FormatterLogCapture(RoleRadarConsoleFormatter.Create("identity", LogFormat.Json));
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(builder => builder.Logging.AddProvider(capture));
        return (app, client, dbDir, capture);
    }

    private static async Task StopAsync(WebApplication app, HttpClient client, string dbDir)
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

    private static List<JsonElement> Records(FormatterLogCapture capture) =>
        capture.Lines.Select(line => JsonDocument.Parse(line).RootElement.Clone()).ToList();

    private static List<JsonElement> RequestCompleted(FormatterLogCapture capture, string correlationId) =>
        Records(capture)
            .Where(r => r.TryGetProperty("event", out var e) && e.GetString() == "RequestCompleted")
            .Where(r => r.TryGetProperty("correlationId", out var c) && c.GetString() == correlationId)
            .ToList();

    private static string HeaderOf(HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues(CorrelationId.HeaderName, out var values), $"Missing {CorrelationId.HeaderName} on {(int)response.StatusCode}.");
        var id = Assert.Single(values);
        Assert.True(CorrelationId.TryAccept(id, out _), $"Header value '{id}' is not a well-formed correlation id.");
        return id;
    }

    [Fact]
    public async Task LoginPage_Discovery_AndTokenError_CarryTheHeader()
    {
        var (app, client, dbDir, capture) = await StartAsync();
        try
        {
            var login = await client.GetAsync("/Account/Login");
            var discovery = await client.GetAsync("/.well-known/openid-configuration");
            var tokenError = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "roleradar-web",
                ["code"] = "not-a-real-code",
            }));

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
            Assert.True((int)tokenError.StatusCode >= 400);

            var ids = new[] { HeaderOf(login), HeaderOf(discovery), HeaderOf(tokenError) };

            // Non-vacuous capture first.
            Assert.NotEmpty(capture.Lines);
            Assert.NotEmpty(RequestCompleted(capture, ids[0]));
        }
        finally
        {
            await StopAsync(app, client, dbDir);
        }
    }

    [Fact]
    public async Task InboundIds_AreAcceptedOrReplaced_ByTheSameRules()
    {
        var (app, client, dbDir, capture) = await StartAsync();
        try
        {
            using var good = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
            good.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, "test-corr-idp-0001");
            Assert.Equal("test-corr-idp-0001", HeaderOf(await client.SendAsync(good)));

            using var bad = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
            bad.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, "bad id<script>");
            var replaced = HeaderOf(await client.SendAsync(bad));

            Assert.NotEqual("bad id<script>", replaced);
            Assert.NotEmpty(capture.Lines);
            Assert.NotEmpty(RequestCompleted(capture, replaced));
            Assert.DoesNotContain("bad id", capture.Text, StringComparison.Ordinal);
        }
        finally
        {
            await StopAsync(app, client, dbDir);
        }
    }

    [Fact]
    public async Task AuthorizeRequestRecord_HasNoQueryString()
    {
        var (app, client, dbDir, capture) = await StartAsync();
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                "/connect/authorize?client_id=roleradar-web&response_type=code&scope=openid&redirect_uri=https%3A%2F%2Flocalhost%3A5003%2Fauth%2Fcallback&code_challenge=SENTINEL-CHALLENGE&code_challenge_method=S256&state=SENTINEL-STATE");
            request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, "test-corr-idp-0002");
            await client.SendAsync(request);

            Assert.NotEmpty(capture.Lines);
            var record = Assert.Single(RequestCompleted(capture, "test-corr-idp-0002"));
            Assert.Equal("/connect/authorize", record.GetProperty("props").GetProperty("path").GetString());
            var leaking = capture.Lines.Where(l => l.Contains("SENTINEL-", StringComparison.Ordinal)).ToList();
            Assert.True(leaking.Count == 0, $"Query values reached the log:\n{string.Join("\n", leaking)}");
        }
        finally
        {
            await StopAsync(app, client, dbDir);
        }
    }

    [Fact]
    public async Task EveryRecord_WrittenDuringALoginPageRequest_CarriesItsCorrelationId()
    {
        var (app, client, dbDir, capture) = await StartAsync();
        try
        {
            await client.GetAsync("/Account/Login");
            capture.Clear();

            using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login");
            request.Headers.TryAddWithoutValidation(CorrelationId.HeaderName, "test-corr-idp-0003");
            await client.SendAsync(request);

            var records = Records(capture);
            Assert.NotEmpty(records);
            Assert.NotEmpty(RequestCompleted(capture, "test-corr-idp-0003"));
            Assert.All(records, r => Assert.Equal("test-corr-idp-0003", r.GetProperty("correlationId").GetString()));
        }
        finally
        {
            await StopAsync(app, client, dbDir);
        }
    }
}
