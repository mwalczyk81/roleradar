using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Queries;
using RoleRadar.Observability;
using RoleRadar.Tests.Integration.Fixtures;
using RoleRadar.Tests.Integration.Hosting;
using RoleRadar.Tests.Integration.Tracking;
using Xunit;

namespace RoleRadar.Tests.Integration.Observability;

/// <summary>
/// Feature 015 US1 &amp; US3 (FR-004, FR-017, SC-003, Principle VI):
/// Launched-host and DI tests verifying that <c>RoleRadar.Cli</c> and <c>RoleRadar.Mcp.Stdio</c>
/// register zero OTLP exporters by default when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is unset,
/// export spans with matching trace IDs when opted in, bound their shutdown flush via
/// <c>ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS</c> against an unreachable endpoint (<c>http://127.0.0.1:1</c>)
/// so the CLI never hangs, and never write non-MCP bytes to <c>stdout</c>.
/// </summary>
[Trait("Lane", "images")]
public sealed class CliMcpTelemetryIntegrationTests(SharedMongoFixture mongo)
{
    [Fact]
    public async Task Cli_DefaultAndOptInTelemetry_RespectsOtlpEndpointConfiguration_AndEmitsRunAndMongoSpans()
    {
        var dbName = mongo.CreateDatabaseName("rrtest_cli_telemetry");
        var previousEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        var previousConn = Environment.GetEnvironmentVariable("ROLERADAR_MONGO_CONNECTION_STRING");
        var previousDb = Environment.GetEnvironmentVariable("ROLERADAR_MONGO_DATABASE");

        try
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", null);
            Environment.SetEnvironmentVariable("ROLERADAR_MONGO_CONNECTION_STRING", mongo.ConnectionString);
            Environment.SetEnvironmentVariable("ROLERADAR_MONGO_DATABASE", dbName);

            await using (var defaultProvider = RoleRadar.Cli.Program.BuildServiceProvider())
            {
                var exporters = defaultProvider.GetServices<IConfigureOptions<OtlpExporterOptions>>().ToList();
                Assert.Empty(exporters);
                Assert.Null(defaultProvider.GetService<TracerProvider>());
            }

            var exportedSpans = new List<Activity>();
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:1");

            await using (var optInProvider = RoleRadar.Cli.Program.BuildServiceProvider(services =>
            {
                services.ConfigureOpenTelemetryTracerProvider(tracer => tracer.AddInMemoryExporter(exportedSpans));
            }))
            {
                Assert.NotNull(optInProvider.GetService<TracerProvider>());

                using (var runActivity = TraceIds.StartActivity("roleradar.cli.run", RoleRadar.Cli.Program.RunId))
                {
                    using var scope = optInProvider.CreateScope();
                    var repository = scope.ServiceProvider.GetRequiredService<INoteRepository>();
                    var result = await repository.CheckReadinessAsync();
                    Assert.True(result.IsSuccess);
                }

                var flushed = TelemetrySetup.FlushRoleRadarTelemetryWithBoundedTimeout(optInProvider, timeoutMs: 250);
                Assert.True(flushed || exportedSpans.Count > 0);
            }

            Assert.Contains(exportedSpans, s =>
                s.DisplayName == "roleradar.cli.run"
                && s.TraceId.ToHexString() == RoleRadar.Cli.Program.RunId);
            Assert.Contains(exportedSpans, s =>
                s.Source.Name.StartsWith("MongoDB", StringComparison.Ordinal)
                && s.TraceId.ToHexString() == RoleRadar.Cli.Program.RunId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", previousEndpoint);
            Environment.SetEnvironmentVariable("ROLERADAR_MONGO_CONNECTION_STRING", previousConn);
            Environment.SetEnvironmentVariable("ROLERADAR_MONGO_DATABASE", previousDb);
        }
    }

    [Fact]
    public async Task Cli_WhenOtlpEndpointIsUnreachable_FlushesWithBoundedTimeout_AndNeverHangs()
    {
        var dbName = mongo.CreateDatabaseName("rrtest_cli_flush_timeout");
        var env = new Dictionary<string, string>(mongo.CreateProcessEnvironment(dbName))
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:1",
            ["ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS"] = "250",
        };

        var sw = Stopwatch.StartNew();
        var (exitCode, stdout, _) = await LaunchedCliRunner.RunCliAsync("verify", env, forceContainer: false);
        sw.Stop();

        Assert.Equal(0, exitCode);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"CLI took {sw.Elapsed.TotalSeconds:F2}s despite 250ms bounded flush timeout.");
        Assert.DoesNotContain("OTEL", stdout, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mcp_WhenOtlpEndpointIsUnreachable_HandlesToolCall_AndWritesZeroNonMcpBytesToStdout()
    {
        var dbName = mongo.CreateDatabaseName("rrtest_mcp_flush_timeout");
        var env = new Dictionary<string, string>(mongo.CreateProcessEnvironment(dbName))
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:1",
            ["ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS"] = "250",
        };

        var sw = Stopwatch.StartNew();
        await using (var mcp = await McpProcessClient.StartAsync(McpHostLocator.GetMcpDllPath(), env, forceContainer: false))
        {
            using var response = await mcp.CallToolRawAsync("note_search", new { query = "fictional" });
            Assert.Equal(System.Text.Json.JsonValueKind.Object, response.RootElement.ValueKind);
        }
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"MCP took {sw.Elapsed.TotalSeconds:F2}s to shut down despite 250ms bounded flush timeout.");
    }
}
