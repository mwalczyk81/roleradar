using Microsoft.AspNetCore.Diagnostics;
using RoleRadar.Api.Envelopes;
using RoleRadar.Infrastructure.Mongo;

namespace RoleRadar.Api.Observability;

/// <summary>
/// Feature 013 (FR-024, contracts/http-correlation-and-errors.md): an exception that escapes a request becomes the standard
/// envelope. The data store not answering is a 503 <c>service_unavailable</c> with <c>Retry-After</c>; anything else is a
/// 500 <c>internal_error</c>. The body is fixed text and the correlation id. The exception itself, with its message and
/// stack, goes to the log, where the scrubbing formatter removes anything that must not be kept. A client that has gone
/// away is absorbed quietly, because nobody is left to answer and it is not an error.
/// </summary>
public sealed partial class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    [LoggerMessage(EventId = 6002, EventName = "DatastoreUnavailable", Level = LogLevel.Warning,
        Message = "The data store did not answer; the request was answered with 503")]
    private static partial void LogDatastoreUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6003, EventName = "UnhandledRequestException", Level = LogLevel.Error,
        Message = "The request failed with an unhandled exception; it was answered with 500")]
    private static partial void LogUnhandled(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6004, EventName = "RequestAbortedByClient", Level = LogLevel.Debug,
        Message = "The client closed the request before it finished")]
    private static partial void LogClientAborted(ILogger logger);

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogClientAborted(logger);
            return true;
        }

        var correlationId = CorrelationMiddleware.Current(httpContext) ?? string.Empty;

        IResult result;
        if (DatastoreFailure.IsUnavailable(exception))
        {
            LogDatastoreUnavailable(logger, exception);
            result = ErrorEnvelope.ServiceUnavailableResult(ErrorEnvelope.ServiceUnavailable() with { CorrelationId = correlationId });
        }
        else
        {
            LogUnhandled(logger, exception);
            result = ErrorEnvelope.InternalErrorResult(ErrorEnvelope.InternalError() with { CorrelationId = correlationId });
        }

        await result.ExecuteAsync(httpContext);
        return true;
    }
}
