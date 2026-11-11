using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Trace;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>
/// Feature 015 Gate 2 (FR-001, FR-003, FR-005, FR-017, SC-003): Unit tests for <see cref="TelemetrySetup"/>.
/// Verifies default-off OTLP exporter registration when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is unset or whitespace,
/// canonical <c>OTEL_SERVICE_NAME</c> resolution, <c>ROLERADAR_TELEMETRY_FLUSH_TIMEOUT_MS</c> clamping (100..5000, default 2000),
/// and 100% local trace sampling (<see cref="AlwaysOnSampler"/>).
/// </summary>
public sealed class TelemetrySetupTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenOtelExporterOtlpEndpointIsUnsetOrWhitespace_ZeroOtlpExportersAreRegistered(string? endpointValue)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TelemetrySetup.OtlpEndpointVariable] = endpointValue
            })
            .Build();

        Assert.False(TelemetrySetup.IsOtlpEndpointConfigured(endpointValue));

        services.AddRoleRadarTelemetry("roleradar-api", config);
        using var sp = services.BuildServiceProvider();
        var tracerProvider = sp.GetRequiredService<TracerProvider>();
        Assert.NotNull(tracerProvider);

        var otlpExporterServices = services
            .Where(d => d.ServiceType.FullName?.Contains("Otlp", StringComparison.OrdinalIgnoreCase) == true
                     || d.ImplementationType?.FullName?.Contains("Otlp", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.Empty(otlpExporterServices);
    }

    [Fact]
    public void WhenOtelExporterOtlpEndpointIsSet_OtlpTraceExporterIsRegistered()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TelemetrySetup.OtlpEndpointVariable] = "http://127.0.0.1:4317"
            })
            .Build();

        Assert.True(TelemetrySetup.IsOtlpEndpointConfigured("http://127.0.0.1:4317"));

        services.AddRoleRadarTelemetry("roleradar-api", config);
        using var sp = services.BuildServiceProvider();
        var options = sp.GetService<IOptionsMonitor<OtlpExporterOptions>>();
        Assert.NotNull(options);
        Assert.Equal(new Uri("http://127.0.0.1:4317"), options!.CurrentValue.Endpoint);
    }

    [Theory]
    [InlineData("roleradar-web", null, "roleradar-web")]
    [InlineData("roleradar-api", "", "roleradar-api")]
    [InlineData("roleradar-identity", "   ", "roleradar-identity")]
    [InlineData("roleradar-cli", null, "roleradar-cli")]
    [InlineData("roleradar-mcp", null, "roleradar-mcp")]
    [InlineData("roleradar-api", "custom-service-override", "custom-service-override")]
    public void ResolveServiceName_UsesCanonicalHostDefault_UnlessOtelServiceNameIsSet(
        string defaultServiceName,
        string? configuredName,
        string expected)
    {
        Assert.Equal(expected, TelemetrySetup.ResolveServiceName(defaultServiceName, configuredName));
    }

    [Theory]
    [InlineData(null, 2000)]
    [InlineData("", 2000)]
    [InlineData("not-a-number", 2000)]
    [InlineData("0", 100)]
    [InlineData("-50", 100)]
    [InlineData("50", 100)]
    [InlineData("100", 100)]
    [InlineData("1500", 1500)]
    [InlineData("2000", 2000)]
    [InlineData("5000", 5000)]
    [InlineData("99999", 5000)]
    public void ResolveFlushTimeoutMs_DefaultsTo2000_AndClampsBetween100And5000(string? raw, int expected)
    {
        Assert.Equal(expected, TelemetrySetup.ResolveFlushTimeoutMs(raw));
    }

    [Fact]
    public void SamplingPolicy_Samples100PercentOfTracesLocally_AcrossRegisteredSources()
    {
        var exported = new List<Activity>();
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddRoleRadarTelemetry(
            "roleradar-api",
            config,
            tracing => tracing.AddInMemoryExporter(exported));

        using var sp = services.BuildServiceProvider();
        var tracerProvider = sp.GetRequiredService<TracerProvider>();

        using (var activity = TraceIds.StartActivity("roleradar.test.sampled"))
        {
            Assert.True(activity.IsAllDataRequested);
            Assert.Equal(ActivityTraceFlags.Recorded, activity.ActivityTraceFlags & ActivityTraceFlags.Recorded);
        }

        tracerProvider.ForceFlush();
        var span = Assert.Single(exported);
        Assert.Equal("roleradar.test.sampled", span.OperationName);
    }
}
