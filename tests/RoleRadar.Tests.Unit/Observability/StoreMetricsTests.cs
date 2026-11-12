using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using RoleRadar.Domain.Common;
using RoleRadar.Observability;
using Xunit;

namespace RoleRadar.Tests.Unit.Observability;

/// <summary>
/// Feature 015 Gate 4 (RR-69, RR-72, ADR 0052):
/// Unit tests for <see cref="RoleRadarStoreMetrics"/> datastore command and readiness health instruments.
/// </summary>
public sealed class StoreMetricsTests
{
    [Fact]
    public void RecordOperation_EmitsHistogramMeasurement_WithCorrectTags()
    {
        var exported = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(RoleRadarStoreMetrics.MeterName)
            .AddInMemoryExporter(exported)
            .Build();

        RoleRadarStoreMetrics.RecordOperation(
            durationSeconds: 0.042,
            systemName: RoleRadarStoreMetrics.SystemMongoDb,
            operationName: "find",
            collectionName: "postings",
            errorType: null);

        meterProvider.ForceFlush();

        var metric = Assert.Single(exported, m => m.Name == RoleRadarStoreMetrics.OperationDurationInstrument);
        Assert.Equal(MetricType.Histogram, metric.MetricType);

        var points = new List<MetricPoint>();
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            points.Add(point);
        }

        var p = Assert.Single(points);
        Assert.Equal(1, p.GetHistogramCount());
        Assert.Equal(0.042, p.GetHistogramSum(), precision: 3);

        var tags = new Dictionary<string, object?>();
        foreach (var tag in p.Tags)
        {
            tags[tag.Key] = tag.Value;
        }

        Assert.Equal("mongodb", tags["db.system.name"]);
        Assert.Equal("find", tags["db.operation.name"]);
        Assert.Equal("postings", tags["db.collection.name"]);
        Assert.False(tags.ContainsKey("error.type"));
    }

    [Fact]
    public void RecordReadiness_EmitsStatusAndDurationMeasurements_WithCorrectTags()
    {
        RoleRadarStoreMetrics.ResetForTesting();
        var exported = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(RoleRadarStoreMetrics.MeterName)
            .AddInMemoryExporter(exported)
            .Build();

        RoleRadarStoreMetrics.RecordReadiness(
            systemName: RoleRadarStoreMetrics.SystemSqlite,
            healthy: true,
            durationSeconds: 0.015);

        meterProvider.ForceFlush();

        var statusMetric = Assert.Single(exported, m => m.Name == RoleRadarStoreMetrics.ReadinessStatusInstrument);
        var durationMetric = Assert.Single(exported, m => m.Name == RoleRadarStoreMetrics.ReadinessDurationInstrument);

        var statusPoints = new List<MetricPoint>();
        foreach (ref readonly var point in statusMetric.GetMetricPoints())
        {
            statusPoints.Add(point);
        }

        var sp = Assert.Single(statusPoints);
        var statusTags = new Dictionary<string, object?>();
        foreach (var tag in sp.Tags)
        {
            statusTags[tag.Key] = tag.Value;
        }
        Assert.Equal("sqlite", statusTags["db.system.name"]);
        Assert.Equal("healthy", statusTags["status"]);

        var durationPoints = new List<MetricPoint>();
        foreach (ref readonly var point in durationMetric.GetMetricPoints())
        {
            durationPoints.Add(point);
        }

        var dp = Assert.Single(durationPoints);
        var durationTags = new Dictionary<string, object?>();
        foreach (var tag in dp.Tags)
        {
            durationTags[tag.Key] = tag.Value;
        }
        Assert.Equal("sqlite", durationTags["db.system.name"]);
        Assert.Equal("healthy", durationTags["status"]);
        Assert.Equal(0.015, dp.GetHistogramSum(), precision: 3);
    }

    [Fact]
    public void RecordReadiness_TwoConsecutiveProbes_ObservableGaugeExportsLastStatusNotSum()
    {
        RoleRadarStoreMetrics.ResetForTesting();
        var exported = new List<Metric>();
        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(RoleRadarStoreMetrics.MeterName)
            .AddInMemoryExporter(exported)
            .Build();

        // First probe: healthy
        RoleRadarStoreMetrics.RecordReadiness(
            systemName: "consecutive-test-system",
            healthy: true,
            durationSeconds: 0.010);

        // Second probe: unhealthy
        RoleRadarStoreMetrics.RecordReadiness(
            systemName: "consecutive-test-system",
            healthy: false,
            durationSeconds: 0.012);

        meterProvider.ForceFlush();

        var statusMetric = Assert.Single(exported, m => m.Name == RoleRadarStoreMetrics.ReadinessStatusInstrument);
        Assert.Equal(MetricType.LongGauge, statusMetric.MetricType);

        var statusPoints = new List<MetricPoint>();
        foreach (ref readonly var point in statusMetric.GetMetricPoints())
        {
            statusPoints.Add(point);
        }

        var sp = Assert.Single(statusPoints, p =>
        {
            foreach (var tag in p.Tags)
            {
                if (tag.Key == "db.system.name" && Equals(tag.Value, "consecutive-test-system"))
                {
                    return true;
                }
            }
            return false;
        });

        // The exported value must be 0 (unhealthy), NOT 1 (healthy) or 1 + 0 = 1 (sum)
        Assert.Equal(0, sp.GetGaugeLastValueLong());
    }
}
