#pragma warning disable CA1848, CA1873

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Contract.Observability;

/// <summary>
/// Feature 015 Gate 3 (RR-70, RR-86, RR-72, FR-004, FR-005, SC-001):
/// Contract tests (no Docker) verifying that OpenTelemetry logging over OTLP:
/// 1. Registers zero OTLP log exporters when OTEL_EXPORTER_OTLP_ENDPOINT is unset or empty.
/// 2. Binds the ambient Activity.Current TraceId and SpanId to emitted LogRecords and preserves service.name.
/// 3. Preserves MCP stdout hygiene: Mcp.Stdio writes zero non-MCP bytes to stdout even when OTLP logging is enabled.
/// </summary>
public sealed class OtlpLogCorrelationContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenOtelExporterOtlpEndpointIsUnsetOrEmpty_ZeroOtlpLogExportersAreRegistered(string? endpointValue)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TelemetrySetup.OtlpEndpointVariable] = endpointValue
            })
            .Build();

        services.AddLogging(builder => builder.AddRoleRadarLogging("api", LogStream.Split, config));

        using var sp = services.BuildServiceProvider();

        var otlpExporterServices = services
            .Where(d => d.ServiceType.FullName?.Contains("Otlp", StringComparison.OrdinalIgnoreCase) == true
                     || d.ImplementationType?.FullName?.Contains("Otlp", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.Empty(otlpExporterServices);
    }

    [Theory]
    [InlineData("api", "roleradar-api")]
    [InlineData("identity", "roleradar-identity")]
    public void WhenOtelExporterOtlpEndpointIsSet_LogRecordCarriesAmbientTraceIdAndSpanId_AndResourceServiceNameMatches(
        string service,
        string expectedServiceName)
    {
        var exported = new List<LogRecord>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TelemetrySetup.OtlpEndpointVariable] = "http://127.0.0.1:4318"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddRoleRadarLogging(service, LogStream.Split, config, logging => logging.AddInMemoryExporter(exported));
        });

        using var sp = services.BuildServiceProvider();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("ContractTestCategory");

        using var activity = TraceIds.StartActivity("contract.test.activity");
        Assert.NotNull(activity);

        logger.LogInformation("Processing contract test event");

        var record = Assert.Single(exported);
        Assert.Equal(activity.TraceId, record.TraceId);
        Assert.Equal(activity.SpanId, record.SpanId);

        var resolved = TelemetrySetup.ResolveServiceName(
            service.StartsWith("roleradar-", StringComparison.OrdinalIgnoreCase) ? service : $"roleradar-{service}",
            config);
        Assert.Equal(expectedServiceName, resolved);
    }

    [Fact]
    public void McpStdio_WhenOtlpLoggingIsEnabled_WritesZeroNonMcpBytesToStdout()
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;

        using var stdoutCapture = new StringWriter();
        using var stderrCapture = new StringWriter();

        try
        {
            Console.SetOut(stdoutCapture);
            Console.SetError(stderrCapture);

            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [TelemetrySetup.OtlpEndpointVariable] = "http://127.0.0.1:4318"
                })
                .Build();

            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddRoleRadarLogging("mcp", LogStream.StdErr, config));

            using (var sp = services.BuildServiceProvider())
            {
                var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("McpContractTestCategory");
                logger.LogInformation("MCP diagnostic log message");
            }

            // Stdout hygiene: 0 non-MCP bytes from MCP logger to stdout
            var stdoutOutput = stdoutCapture.ToString();
            Assert.DoesNotContain("MCP diagnostic log message", stdoutOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("\"service\":\"mcp\"", stdoutOutput, StringComparison.Ordinal);
            Assert.DoesNotContain(" mcp ", stdoutOutput, StringComparison.Ordinal);

            // Stderr receives the console log line
            var stderrOutput = stderrCapture.ToString();
            Assert.Contains("MCP diagnostic log message", stderrOutput, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }
}
