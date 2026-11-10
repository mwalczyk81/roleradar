using RoleRadar.Application.Abstractions;

namespace RoleRadar.Api.Envelopes;

public sealed record ErrorFieldDetail(
    string Field,
    string Issue);

public sealed record ErrorEnvelope(
    bool Ok,
    string ErrorCode,
    string Message,
    IReadOnlyList<ErrorFieldDetail>? Details = null)
{
    /// <summary>
    /// The request's X-Correlation-ID (feature 013, contracts/http-correlation-and-errors.md). Filled from the current
    /// request by <see cref="ErrorResults"/>, so every producer of an envelope carries it without passing it along.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Required]
    public string CorrelationId { get; init; } = ErrorResults.CurrentCorrelationId();

    public static ErrorEnvelope Validation(string message, IReadOnlyList<ErrorFieldDetail>? details = null) =>
        new(false, "validation_error", message, details);

    public static ErrorEnvelope NotFound(string message) =>
        new(false, "not_found", message);

    public static ErrorEnvelope ConcurrencyConflict(string message) =>
        new(false, "concurrency_conflict", message);

    /// <summary>The data store did not answer. The message is fixed: no endpoint, exception text or remediation command.</summary>
    public static ErrorEnvelope ServiceUnavailable() =>
        new(false, "service_unavailable", "The data store is not reachable. Try again shortly.");

    /// <summary>Any other failure the owner cannot fix. The message is fixed; the detail is in the scrubbed log.</summary>
    public static ErrorEnvelope InternalError() =>
        new(false, "internal_error", "The request failed unexpectedly. Try again, and check the logs if it keeps happening.");

    /// <summary>The seconds a client should wait before retrying a 503 (contracts/http-correlation-and-errors.md).</summary>
    public const int RetryAfterSeconds = 5;

    /// <summary>A 503 with the envelope and <c>Retry-After</c>, for a request whose data store did not answer.</summary>
    public static IResult ServiceUnavailableResult(ErrorEnvelope? envelope = null) =>
        new HeaderAddingResult(
            StatusCodes.Status503ServiceUnavailable,
            envelope ?? ServiceUnavailable(),
            retryAfterSeconds: RetryAfterSeconds);

    /// <summary>A 500 with the envelope.</summary>
    public static IResult InternalErrorResult(ErrorEnvelope? envelope = null) =>
        new HeaderAddingResult(StatusCodes.Status500InternalServerError, envelope ?? InternalError(), retryAfterSeconds: null);

    private sealed class HeaderAddingResult(int statusCode, ErrorEnvelope envelope, int? retryAfterSeconds) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            if (retryAfterSeconds is { } seconds)
            {
                httpContext.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return Results.Json(envelope, statusCode: statusCode).ExecuteAsync(httpContext);
        }
    }

    public static IResult FromResultError(ResultError? error)
    {
        if (error is null)
        {
            return Results.BadRequest(Validation("The request payload failed validation."));
        }

        if (error.Value.Code == RoleRadar.Application.Abstractions.ErrorCode.ValidationError
            && error.Value.Metadata is not null
            && error.Value.Metadata.TryGetValue("field", out var field)
            && field is string fieldName
            && !string.IsNullOrWhiteSpace(fieldName))
        {
            return Results.BadRequest(Validation(
                error.Value.Message,
                [new ErrorFieldDetail(fieldName, error.Value.Message)]));
        }

        return error.Value.Code switch
        {
            RoleRadar.Application.Abstractions.ErrorCode.NotFound =>
                Results.NotFound(NotFound(error.Value.Message)),
            RoleRadar.Application.Abstractions.ErrorCode.StaleVersionToken =>
                Results.Conflict(ConcurrencyConflict(error.Value.Message)),
            // The Result message names the endpoint and a remediation command for the CLI; none of that leaves the API.
            RoleRadar.Application.Abstractions.ErrorCode.DatastoreOffline =>
                ServiceUnavailableResult(),
            _ =>
                Results.BadRequest(Validation(error.Value.Message))
        };
    }
}
