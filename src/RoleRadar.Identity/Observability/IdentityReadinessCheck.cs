using Microsoft.Extensions.Diagnostics.HealthChecks;
using RoleRadar.Identity.Data;

namespace RoleRadar.Identity.Observability;

/// <summary>
/// Feature 013 (FR-023, contracts/health.md): readiness of the identity service is whether its SQLite database can be
/// reached. Bounded at 2 s. The result carries no description, path or exception text.
/// </summary>
public sealed class IdentityReadinessCheck(IdentityAppDbContext database) : IHealthCheck
{
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(Bound);

        try
        {
            return await database.Database.CanConnectAsync(bounded.Token) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy();
        }
    }
}
