using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

/// <summary>
/// Feature 014 US2 (FR-023 to FR-027, ADR 0044), replacing the 013 identity correlation tests: the identity service honours a
/// valid <c>traceparent</c>, replaces a bad one without logging it, ignores <c>X-Correlation-ID</c> and returns no header
/// that carries the trace id. Its log records carry the field <c>traceId</c>.
/// </summary>
public sealed class IdentityTraceContextContractTests
{
    private const string TraceId = "0af7651916cd43dd8448eb211c80319c";
    private const string Traceparent = "00-" + TraceId + "-b7ad6b7169203331-01";

    private static readonly Regex HexTraceId = new("^[0-9a-f]{32}$", RegexOptions.CultureInvariant);

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

    private static List<JsonElement> RequestCompleted(FormatterLogCapture capture, string traceId) =>
        Records(capture)
            .Where(r => r.TryGetProperty("event", out var e) && e.GetString() == "RequestCompleted")
            .Where(r => r.TryGetProperty("traceId", out var c) && c.GetString() == traceId)
            .ToList();

    private static HttpRequestMessage Get(string url, string? traceparent)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (traceparent is not null)
        {
            request.Headers.TryAddWithoutValidation("traceparent", traceparent);
        }

        return request;
    }

    [Fact]
    public async Task AValidInboundTraceparent_IsHonoured_AndNoResponseHeaderReturnsTheTraceId()
    {
        var (app, client, dbDir, capture) = await StartAsync();
        try
        {
            using var request = Get("/Account/Login", Traceparent);
            request.Headers.TryAddWithoutValidation("X-Correlation-ID", "legacy-corr-ignored-0002");
            using var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("X-Correlation-ID"), "X-Correlation-ID must not be returned.");
            Assert.DoesNotContain(response.Headers, h => h.Value.Any(v => v.Contains(TraceId, StringComparison.OrdinalIgnoreCase)));

            // Non-vacuous capture first.
            Assert.NotEmpty(capture.Lines);
            Assert.NotEmpty(RequestCompleted(capture, TraceId));
            Assert.DoesNotContain("legacy-corr-ignored-0002", capture.Text, StringComparison.Ordinal);
        }
        finally
        {
            await StopAsync(app, client, dbDir);
        }
    }

    [Theory]
    [InlineData("not-a-traceparent")]
    [InlineData("00-00000000000000000000000000000000-b7ad6b7169203331-01")]
    [InlineData("00-0AF7651916CD43DD8448EB211C80319C-b7ad6b7169203331-01")]
    public async Task ABadTraceparent_IsReplaced_ByAGeneratedId_AndNeverLogged(string bad)
    {
        var (app, client, dbDir, capture) = await StartAsync();
        try
        {
            using var response = await client.SendAsync(Get("/Account/Login", bad));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            Assert.NotEmpty(capture.Lines);
            var completed = Records(capture)
                .Where(r => r.TryGetProperty("event", out var e) && e.GetString() == "RequestCompleted")
                .ToList();
            var record = Assert.Single(completed, r => r.GetProperty("props").GetProperty("path").GetString() == "/Account/Login");
            var replaced = record.GetProperty("traceId").GetString();
            Assert.Matches(HexTraceId, replaced!);
            Assert.NotEqual(new string('0', 32), replaced);
            Assert.DoesNotContain(bad, capture.Text, StringComparison.Ordinal);
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
            using var request = Get(
                "/connect/authorize?client_id=roleradar-web&response_type=code&scope=openid&redirect_uri=https%3A%2F%2Flocalhost%3A5003%2Fauth%2Fcallback&code_challenge=SENTINEL-CHALLENGE&code_challenge_method=S256&state=SENTINEL-STATE",
                Traceparent);
            await client.SendAsync(request);

            Assert.NotEmpty(capture.Lines);
            var record = Assert.Single(RequestCompleted(capture, TraceId));
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
    public async Task EveryRecord_WrittenDuringALoginPageRequest_CarriesItsTraceId_AndNoCorrelationId()
    {
        var (app, client, dbDir, capture) = await StartAsync();
        try
        {
            using var warm = await client.GetAsync("/Account/Login");
            capture.Clear();

            using var response = await client.SendAsync(Get("/Account/Login", Traceparent));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var records = Records(capture);
            Assert.NotEmpty(records);
            Assert.NotEmpty(RequestCompleted(capture, TraceId));
            Assert.All(records, r => Assert.Equal(TraceId, r.GetProperty("traceId").GetString()));
            Assert.DoesNotContain(records, r => r.TryGetProperty("correlationId", out _));
        }
        finally
        {
            await StopAsync(app, client, dbDir);
        }
    }
}
