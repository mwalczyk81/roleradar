using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RoleRadar.Observability;
using RoleRadar.Tests.Contract.Fixtures;
using Xunit;

namespace RoleRadar.Tests.Contract.Api;

/// <summary>
/// Feature 013 US3 (FR-023, contracts/health.md): the API has anonymous <c>/health/live</c> (no checks), <c>/health/ready</c>
/// (the Mongo readiness probe) and <c>/health</c> (an alias of live). Bodies are the minimal status document only.
/// </summary>
public sealed class ApiHealthContractTests(SharedMongoFixture sharedMongo)
{
    private const string ExpectedDefaultUrl = "http://127.0.0.1:8080/health/ready";

    private readonly SharedMongoFixture _sharedMongo = sharedMongo;

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

    private static async Task<(HttpStatusCode Status, string Body, HttpResponseMessage Response)> GetAnonymousAsync(ApiContractTestHost host, string path)
    {
        using var anonymous = new HttpClient { BaseAddress = host.Client.BaseAddress };
        var response = await anonymous.GetAsync(path, TestContext.Current.CancellationToken);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), response);
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
    public async Task LiveAndTheAlias_Return200Healthy_ToAnAnonymousCaller(string path)
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_health_live");

        var (status, body, response) = await GetAnonymousAsync(host, path);

        Assert.Equal(HttpStatusCode.OK, status);
        AssertStatusDocument(body, "Healthy");
        Assert.False(response.Headers.Contains("X-Correlation-ID"), "Health responses carry no X-Correlation-ID since feature 014.");
    }

    [Fact]
    public async Task Ready_Returns200Healthy_WhenMongoAnswers()
    {
        await using var host = await ApiContractTestHost.StartAsync(_sharedMongo, "rrtest_api_health_ready");

        var (status, body, response) = await GetAnonymousAsync(host, "/health/ready");

        Assert.Equal(HttpStatusCode.OK, status);
        AssertStatusDocument(body, "Healthy");
        Assert.False(response.Headers.Contains("X-Correlation-ID"));
    }

    [Fact]
    public async Task Ready_Returns503Unhealthy_WhenTheReadinessCheckReportsUnhealthy_AndLiveStaysUp()
    {
        await using var host = await ApiContractTestHost.StartAsync(
            _sharedMongo, "rrtest_api_health_stub", builder => ReplaceReadinessWith(builder, HealthStatus.Unhealthy));

        var (readyStatus, readyBody, readyResponse) = await GetAnonymousAsync(host, "/health/ready");
        var (liveStatus, liveBody, _) = await GetAnonymousAsync(host, "/health/live");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyStatus);
        AssertStatusDocument(readyBody, "Unhealthy");
        Assert.False(readyResponse.Headers.Contains("X-Correlation-ID"));
        Assert.Equal(HttpStatusCode.OK, liveStatus);
        AssertStatusDocument(liveBody, "Healthy");
    }

    [Fact]
    public async Task Ready_Returns503Unhealthy_WhenMongoIsUnreachable_WithinTheBound()
    {
        // The API starts without Mongo; the readiness check then reports it, bounded at 2 s.
        await using var host = await ApiContractTestHost.StartAsync(
            _sharedMongo,
            "rrtest_api_health_unreachable",
            builder => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MongoConnection"] = "mongodb://127.0.0.1:1/?directConnection=true"
            }));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var (status, body, _) = await GetAnonymousAsync(host, "/health/ready");
        started.Stop();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        AssertStatusDocument(body, "Unhealthy");
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(3), $"Took {started.Elapsed.TotalMilliseconds:0} ms; the check is bounded at 2 s.");
    }

    [Fact]
    public async Task Bodies_CarryNoHostEndpointOrErrorText()
    {
        await using var host = await ApiContractTestHost.StartAsync(
            _sharedMongo, "rrtest_api_health_body", builder => ReplaceReadinessWith(builder, HealthStatus.Unhealthy));

        foreach (var path in new[] { "/health/live", "/health/ready", "/health" })
        {
            var (_, body, _) = await GetAnonymousAsync(host, path);
            Assert.False(string.IsNullOrWhiteSpace(body), $"{path} returned no body, so there is nothing to check.");

            foreach (var forbidden in new[] { "fictional", "invalid", "27017", "mongodb", "pwsh", "docker", "Exception" })
            {
                Assert.DoesNotContain(forbidden, body, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void TheImageHealthcheck_DefaultsToTheReadyEndpoint()
    {
        // Read by reflection so a missing constant fails this test rather than the build of every test in the project.
        var field = typeof(RoleRadar.Api.Program).GetField("DefaultHealthcheckUrl");

        Assert.NotNull(field);
        Assert.Equal(ExpectedDefaultUrl, field.GetRawConstantValue());
    }
}
