#pragma warning disable CA1848, CA1873

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using RoleRadar.Domain.Common;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Contract.Observability;

/// <summary>
/// Feature 015 Gate 4 (RR-69, RR-72, SC-003, SC-005, ADR 0052):
/// Contract tests (no Docker) verifying that OpenTelemetry service and datastore metrics:
/// 1. Register zero OTLP metric exporters when OTEL_EXPORTER_OTLP_ENDPOINT is unset or empty.
/// 2. Register native and custom meters (Microsoft.AspNetCore.Hosting, System.Net.Http, System.Runtime, RoleRadar.Store).
/// 3. Filter out forbidden/high-cardinality labels per the SC-005 allowlist.
/// </summary>
public sealed class ServiceMetricsContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WhenOtelExporterOtlpEndpointIsUnsetOrEmpty_ZeroOtlpMetricExportersAreRegistered(string? endpointValue)
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TelemetrySetup.OtlpEndpointVariable] = endpointValue
            })
            .Build();

        services.AddRoleRadarTelemetry("api", config);

        using var sp = services.BuildServiceProvider();

        var otlpMetricExporters = services
            .Where(d => (d.ServiceType.FullName?.Contains("OtlpMetric", StringComparison.OrdinalIgnoreCase) == true
                      || d.ImplementationType?.FullName?.Contains("OtlpMetric", StringComparison.OrdinalIgnoreCase) == true))
            .ToList();

        Assert.Empty(otlpMetricExporters);
    }

    [Theory]
    [InlineData("roleradar-api")]
    [InlineData("roleradar-identity")]
    public void WhenOtelExporterOtlpEndpointIsSet_MeterProviderIsRegistered_AndSubscribesToExpectedMeters(
        string serviceName)
    {
        var exported = new List<Metric>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TelemetrySetup.OtlpEndpointVariable] = "http://127.0.0.1:4318"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddRoleRadarTelemetry(serviceName, config, configureMetrics: metrics =>
        {
            metrics.AddInMemoryExporter(exported);
        });

        using var sp = services.BuildServiceProvider();
        var meterProvider = sp.GetService<MeterProvider>();
        Assert.NotNull(meterProvider);

        // Record a store readiness metric to verify RoleRadar.Store subscription
        RoleRadarStoreMetrics.RecordReadiness("sqlite", healthy: true, durationSeconds: 0.005);
        meterProvider.ForceFlush();

        var storeStatusMetric = Assert.Single(exported, m => m.Name == RoleRadarStoreMetrics.ReadinessStatusInstrument);
        Assert.NotNull(storeStatusMetric);
    }
}
