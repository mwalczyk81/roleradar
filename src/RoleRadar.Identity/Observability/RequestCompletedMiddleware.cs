using System.Diagnostics;

namespace RoleRadar.Identity.Observability;

/// <summary>
/// First middleware in the identity pipeline (feature 013, ADR 0041; trimmed in feature 014, ADR 0044): writes one
/// RequestCompleted record per request (method, path without the query string, status, duration). Health paths log at Debug.
/// It carries no correlation logic any more: ASP.NET Core extracts <c>traceparent</c> and starts the request activity, and the
/// console formatter reads the trace id from <see cref="Activity.Current"/>.
/// </summary>
public sealed partial class RequestCompletedMiddleware(RequestDelegate next, ILogger<RequestCompletedMiddleware> logger)
{
    [LoggerMessage(EventId = 6001, EventName = "RequestCompleted", Message = "{method} {path} responded {status} in {durationMs} ms")]
    private static partial void LogRequestCompleted(ILogger logger, LogLevel level, string method, string path, int status, double durationMs);

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

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
}
