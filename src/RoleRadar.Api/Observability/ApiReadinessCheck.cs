using Microsoft.Extensions.Diagnostics.HealthChecks;
using RoleRadar.Application.Abstractions;

namespace RoleRadar.Api.Observability;

/// <summary>
/// Feature 013 (FR-023, contracts/health.md): readiness of the API is whether Mongo answers <c>hello</c> as a writable
/// primary, through the same probe the commands use. Bounded at 2 s. The result carries no description, endpoint or
/// exception text; those stay in the scrubbed log the probe already writes.
/// </summary>
public sealed class ApiReadinessCheck(INoteRepository repository) : IHealthCheck
{
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(Bound);

        try
        {
            var readiness = await repository.CheckReadinessAsync(bounded.Token);
            return readiness.IsSuccess ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
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
