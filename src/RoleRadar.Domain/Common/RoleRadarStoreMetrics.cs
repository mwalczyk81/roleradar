using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RoleRadar.Domain.Common;

/// <summary>
/// Feature 015 Gate 4 (RR-69, RR-72, ADR 0052):
/// Metrics for datastore command duration, readiness status, and readiness probe duration.
/// Defined in Domain so both Infrastructure and Identity may reference them directly without
/// violating layering rules.
/// </summary>
public static class RoleRadarStoreMetrics
{
    public const string MeterName = "RoleRadar.Store";
    public const string OperationDurationInstrument = "roleradar.store.operation.duration";
    public const string ReadinessStatusInstrument = "roleradar.store.readiness.status";
    public const string ReadinessDurationInstrument = "roleradar.store.readiness.duration";

    public const string SystemMongoDb = "mongodb";
    public const string SystemSqlite = "sqlite";
    public const string StatusHealthy = "healthy";
    public const string StatusUnhealthy = "unhealthy";

    public static readonly Meter MeterInstance = new(MeterName);

    public static readonly Histogram<double> OperationDuration =
        MeterInstance.CreateHistogram<double>(
            OperationDurationInstrument,
            unit: "s",
            description: "Duration of datastore operations");

    public static readonly Histogram<double> ReadinessDuration =
        MeterInstance.CreateHistogram<double>(
            ReadinessDurationInstrument,
            unit: "s",
            description: "Duration of datastore readiness probe");

    private static readonly ConcurrentDictionary<string, int> LastReadinessStatusBySystem = new(StringComparer.Ordinal);

    public static readonly ObservableGauge<int> ReadinessStatus =
        MeterInstance.CreateObservableGauge<int>(
            ReadinessStatusInstrument,
            () => LastReadinessStatusBySystem.Select(kvp => new Measurement<int>(
                kvp.Value,
                new KeyValuePair<string, object?>("db.system.name", kvp.Key),
                new KeyValuePair<string, object?>("status", kvp.Value == 1 ? StatusHealthy : StatusUnhealthy))),
            unit: "{status}",
            description: "Datastore readiness status (1 = healthy, 0 = unhealthy)");

    public static void RecordOperation(
        double durationSeconds,
        string systemName,
        string operationName,
        string? collectionName = null,
        string? errorType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        var tags = new TagList
        {
            { "db.system.name", systemName },
            { "db.operation.name", operationName }
        };

        if (!string.IsNullOrWhiteSpace(collectionName))
        {
            tags.Add("db.collection.name", collectionName);
        }

        if (!string.IsNullOrWhiteSpace(errorType))
        {
            tags.Add("error.type", errorType);
        }

        OperationDuration.Record(durationSeconds, tags);
    }

    public static void RecordReadiness(
        string systemName,
        bool healthy,
        double durationSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemName);

        LastReadinessStatusBySystem[systemName] = healthy ? 1 : 0;

        var tags = new TagList
        {
            { "db.system.name", systemName },
            { "status", healthy ? StatusHealthy : StatusUnhealthy }
        };

        ReadinessDuration.Record(durationSeconds, tags);
    }

    public static void ResetForTesting()
    {
        LastReadinessStatusBySystem.Clear();
    }
}
