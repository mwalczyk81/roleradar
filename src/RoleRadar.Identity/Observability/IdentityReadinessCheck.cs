using System.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using RoleRadar.Domain.Common;
using RoleRadar.Identity.Data;

namespace RoleRadar.Identity.Observability;

/// <summary>
/// Feature 013 (FR-023, contracts/health.md) & Feature 015 Gate 4 (RR-69, T062, ADR 0052):
/// Readiness of the identity service is whether its SQLite database can be reached.
/// Bounded at 2 s. Records readiness status and duration to RoleRadarStoreMetrics.
/// The result carries no description, path or exception text.
/// </summary>
public sealed class IdentityReadinessCheck(IdentityAppDbContext database) : IHealthCheck
{
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(Bound);

        var sw = Stopwatch.StartNew();
        try
        {
            var canConnect = await database.Database.CanConnectAsync(bounded.Token);
            sw.Stop();
            RoleRadarStoreMetrics.RecordReadiness(RoleRadarStoreMetrics.SystemSqlite, canConnect, sw.Elapsed.TotalSeconds);
            return canConnect ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            RoleRadarStoreMetrics.RecordReadiness(RoleRadarStoreMetrics.SystemSqlite, false, sw.Elapsed.TotalSeconds);
            return HealthCheckResult.Unhealthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            RoleRadarStoreMetrics.RecordReadiness(RoleRadarStoreMetrics.SystemSqlite, false, sw.Elapsed.TotalSeconds);
            return HealthCheckResult.Unhealthy();
        }
    }
}
