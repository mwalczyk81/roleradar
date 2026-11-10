using System.Globalization;
using Microsoft.AspNetCore.Http;
using RoleRadar.Application.Abstractions;
using RoleRadar.Observability;

namespace RoleRadar.Api.Problems;

/// <summary>
/// The API's error results as RFC 9457 problem documents (feature 014, ADR 0044, contracts/problem-details.md). They replace
/// the 013 error envelope: <c>ok</c> became the HTTP status, <c>message</c> became <c>detail</c>, <c>details</c> became
/// <c>errors</c> and <c>correlationId</c> became <c>traceId</c>, the bare W3C trace id of the request. The media type is
/// <c>application/problem+json</c> whatever the client's <c>Accept</c> header says, so an error always has a body.
/// </summary>
public static class ApiProblems
{
    /// <summary>The seconds a client should wait before retrying a 503.</summary>
    public const int RetryAfterSeconds = 5;

    public const string ServiceUnavailableDetail = "The data store is not reachable. Try again shortly.";
    public const string InternalErrorDetail = "The request failed unexpectedly. Try again, and check the logs if it keeps happening.";

    /// <summary>A 400 for a request that failed validation. <paramref name="errors"/> maps a field name to its issues.</summary>
    public static IResult Validation(string detail, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new ProblemResult(StatusCodes.Status400BadRequest, ProblemTypes.ValidationError, detail, errors, retryAfterSeconds: null);

    public static IResult NotFound(string detail) =>
        new ProblemResult(StatusCodes.Status404NotFound, ProblemTypes.NotFound, detail, errors: null, retryAfterSeconds: null);

    public static IResult ConcurrencyConflict(string detail) =>
        new ProblemResult(StatusCodes.Status409Conflict, ProblemTypes.ConcurrencyConflict, detail, errors: null, retryAfterSeconds: null);

    /// <summary>The data store did not answer. The text is fixed: no endpoint, exception text or remediation command.</summary>
    public static IResult ServiceUnavailable() =>
        new ProblemResult(StatusCodes.Status503ServiceUnavailable, ProblemTypes.ServiceUnavailable, ServiceUnavailableDetail, errors: null, RetryAfterSeconds);

    /// <summary>Any other failure the owner cannot fix. The text is fixed; the detail is in the scrubbed log, under the same trace id.</summary>
    public static IResult InternalError() =>
        new ProblemResult(StatusCodes.Status500InternalServerError, ProblemTypes.InternalError, InternalErrorDetail, errors: null, retryAfterSeconds: null);

    /// <summary>Maps an Application <see cref="ResultError"/> to the problem document the contract names.</summary>
    public static IResult FromResultError(ResultError? error)
    {
        if (error is null)
        {
            return Validation("The request payload failed validation.");
        }

        if (error.Value.Code == ErrorCode.ValidationError
            && error.Value.Metadata is not null
            && error.Value.Metadata.TryGetValue("field", out var field)
            && field is string fieldName
            && !string.IsNullOrWhiteSpace(fieldName))
        {
            return Validation(
                error.Value.Message,
                new Dictionary<string, string[]> { [fieldName] = [error.Value.Message] });
        }

        return error.Value.Code switch
        {
            ErrorCode.NotFound => NotFound(error.Value.Message),
            ErrorCode.StaleVersionToken => ConcurrencyConflict(error.Value.Message),
            // The Result message names the endpoint and a remediation command for the CLI; none of that leaves the API.
            ErrorCode.DatastoreOffline => ServiceUnavailable(),
            _ => Validation(error.Value.Message),
        };
    }

    private sealed class ProblemResult(
        int status,
        string errorCode,
        string detail,
        IReadOnlyDictionary<string, string[]>? errors,
        int? retryAfterSeconds) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            if (retryAfterSeconds is { } seconds)
            {
                httpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
            }

            // Built here, inside the request, so the trace id is the one the request's activity carries.
            var extensions = new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["traceId"] = TraceIds.Current(),
            };

            IResult result = errors is { Count: > 0 }
                ? TypedResults.ValidationProblem(
                    errors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                    detail: detail,
                    type: ProblemTypes.TypeFor(errorCode),
                    extensions: extensions)
                : TypedResults.Problem(
                    detail: detail,
                    statusCode: status,
                    type: ProblemTypes.TypeFor(errorCode),
                    extensions: extensions);

            return result.ExecuteAsync(httpContext);
        }
    }
}
