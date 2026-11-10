using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace RoleRadar.Api.Observability;

/// <summary>
/// Feature 013 (FR-023, contracts/health.md): <c>/health/live</c> runs no checks and <c>/health/ready</c> runs the checks
/// tagged <c>ready</c>. Both are anonymous and answer with the minimal <c>{"status":"..."}</c> body: no host, endpoint,
/// exception text or remediation command. The <c>/health</c> alias of live stays a plain endpoint in Program, so the
/// committed OpenAPI document keeps listing it.
/// </summary>
public static class HealthEndpoints
{
    public const string ReadyTag = "ready";

    public static IEndpointRouteBuilder MapRoleRadarHealth(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/health/live", Options(_ => false)).AllowAnonymous();
        app.MapHealthChecks("/health/ready", Options(registration => registration.Tags.Contains(ReadyTag))).AllowAnonymous();
        return app;
    }

    private static HealthCheckOptions Options(Func<HealthCheckRegistration, bool> predicate) => new()
    {
        Predicate = predicate,
        ResponseWriter = static (context, report) =>
        {
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync($"{{\"status\":\"{report.Status}\"}}");
        }
    };
}
