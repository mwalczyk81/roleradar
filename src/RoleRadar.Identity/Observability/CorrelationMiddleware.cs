using System.Diagnostics;
using RoleRadar.Observability;

namespace RoleRadar.Identity.Observability;

/// <summary>
/// First middleware in the identity pipeline (feature 013, ADR 0041). Accepts a well-formed inbound X-Correlation-ID or
/// creates one, returns it on the response, scopes it into every log record of the request, and writes one
/// RequestCompleted record (method, path without the query string, status, duration). Health paths log at Debug.
/// </summary>
public sealed partial class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public const string ItemKey = "RoleRadar.Identity.CorrelationId";

    [LoggerMessage(EventId = 6001, EventName = "RequestCompleted", Message = "{method} {path} responded {status} in {durationMs} ms")]
    private static partial void LogRequestCompleted(ILogger logger, LogLevel level, string method, string path, int status, double durationMs);

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var inbound = context.Request.Headers[CorrelationId.HeaderName].ToString();
        var correlationId = CorrelationId.TryAccept(inbound, out var accepted) ? accepted : CorrelationId.New();

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[CorrelationId.HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using var scope = logger.BeginScope(new Dictionary<string, object?> { [CorrelationId.ScopeKey] = correlationId });
        var started = Stopwatch.GetTimestamp();
        var failed = false;
        try
        {
            await next(context);
        }
        catch
        {
            failed = true;
            throw;
        }
        finally
        {
            var path = (context.Request.PathBase + context.Request.Path).ToString();
            var level = path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ? LogLevel.Debug : LogLevel.Information;
            if (logger.IsEnabled(level))
            {
                var status = failed && !context.Response.HasStarted ? StatusCodes.Status500InternalServerError : context.Response.StatusCode;
                var durationMs = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3);
                LogRequestCompleted(logger, level, context.Request.Method, path, status, durationMs);
            }
        }
    }

    /// <summary>The current request's correlation id, or null outside a request.</summary>
    public static string? Current(HttpContext? context) =>
        context?.Items.TryGetValue(ItemKey, out var value) == true ? value as string : null;
}
