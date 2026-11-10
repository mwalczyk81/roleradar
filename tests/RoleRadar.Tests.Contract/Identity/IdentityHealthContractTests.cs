using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Contract.Identity;

/// <summary>
/// Feature 013 US3 (FR-023, contracts/health.md): the identity service has anonymous <c>/health/live</c>,
/// <c>/health/ready</c> (its SQLite database) and <c>/health</c>. The host is only ever started on a database that can
/// start, because Identity migrates SQLite at startup; the failing readiness case uses a stub in place of the check.
/// </summary>
public sealed class IdentityHealthContractTests
{
    private const string ExpectedDefaultUrl = "http://127.0.0.1:8080/health/ready";

    private sealed class StubCheck(HealthStatus status) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult(status, description: "fictional-stub-detail-host.fictional.invalid"));
    }

    private static void ReplaceReadinessWith(WebApplicationBuilder builder, HealthStatus status) =>
        builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
        {
            options.Registrations.Clear();
            options.Registrations.Add(new HealthCheckRegistration("stub-ready", _ => new StubCheck(status), failureStatus: null, tags: ["ready"]));
        });

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

    private static void AssertStatusDocument(string body, string expectedStatus)
    {
        using var document = JsonDocument.Parse(body);
        var property = Assert.Single(document.RootElement.EnumerateObject());
        Assert.Equal("status", property.Name);
        Assert.Equal(expectedStatus, property.Value.GetString());
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health")]
    [InlineData("/health/ready")]
    public async Task EachEndpoint_Returns200Healthy_WithTheHeader_WhenTheDatabaseIsOpen(string path)
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync();
        try
        {
            using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
            var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            AssertStatusDocument(body, "Healthy");
            Assert.True(response.Headers.Contains(CorrelationId.HeaderName), "Health responses carry X-Correlation-ID.");
        }
        finally
        {
            await StopAsync(app, client, dbDir);
        }
    }

    [Fact]
    public async Task Ready_Returns503Unhealthy_WhenTheReadinessCheckReportsUnhealthy_AndLiveStaysUp()
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(builder => ReplaceReadinessWith(builder, HealthStatus.Unhealthy));
        try
        {
            using var ready = await client.GetAsync("/health/ready", TestContext.Current.CancellationToken);
            var readyBody = await ready.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            using var live = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
            var liveBody = await live.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
            AssertStatusDocument(readyBody, "Unhealthy");
            Assert.True(ready.Headers.Contains(CorrelationId.HeaderName));
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            AssertStatusDocument(liveBody, "Healthy");
        }
        finally
        {
            await StopAsync(app, client, dbDir);
        }
    }

    [Fact]
    public async Task Bodies_CarryNoHostPathOrErrorText()
    {
        var (app, client, _, dbDir) = await IdentityContractTestServer.StartHostAsync(builder => ReplaceReadinessWith(builder, HealthStatus.Unhealthy));
        try
        {
            foreach (var path in new[] { "/health/live", "/health/ready", "/health" })
            {
                using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
                var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.False(string.IsNullOrWhiteSpace(body), $"{path} returned no body, so there is nothing to check.");

                foreach (var forbidden in new[] { "fictional", "invalid", "identity.db", "sqlite", "Exception", "rrtest_" })
                {
                    Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
                }
            }
        }
        finally
        {
            await StopAsync(app, client, dbDir);
        }
    }

    [Fact]
    public void TheImageHealthcheck_DefaultsToTheReadyEndpoint()
    {
        var field = typeof(RoleRadar.Identity.Program).GetField("DefaultHealthcheckUrl");

        Assert.NotNull(field);
        Assert.Equal(ExpectedDefaultUrl, field.GetRawConstantValue());
    }
}
