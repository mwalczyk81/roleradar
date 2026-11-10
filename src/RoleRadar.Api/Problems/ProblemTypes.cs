using Microsoft.AspNetCore.Http;
using RoleRadar.Observability;

namespace RoleRadar.Api.Problems;

/// <summary>
/// The error codes of the API's RFC 9457 problem documents and the <c>type</c> identifier built from each (feature 014,
/// ADR 0044, <c>docs/api/problems.md</c>). The identifier is a documentation URL in the public repository, one anchor per code.
/// The base is this one constant, so it can move if the API is hosted. The web switches on <c>errorCode</c>, never on
/// <c>type</c>.
/// </summary>
public static class ProblemTypes
{
    /// <summary>The page every <c>type</c> points at, up to and including the anchor marker. The code follows it.</summary>
    public const string TypeUriPrefix = "https://github.com/mwalczyk81/roleradar/blob/main/docs/api/problems.md#";

    public const string ValidationError = "validation_error";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string ConcurrencyConflict = "concurrency_conflict";
    public const string ServiceUnavailable = "service_unavailable";
    public const string InternalError = "internal_error";

    /// <summary>Every code, in the order <c>docs/api/problems.md</c> lists them.</summary>
    public static IReadOnlyList<string> Codes { get; } =
        [ValidationError, Unauthorized, Forbidden, NotFound, ConcurrencyConflict, ServiceUnavailable, InternalError];

    /// <summary>The <c>type</c> of a problem document with this code.</summary>
    public static string TypeFor(string errorCode) => TypeUriPrefix + errorCode;

    /// <summary>
    /// The code for a status the platform answers on its own (an empty 401, 403 or 404 body filled in by the status-code pages),
    /// or null for a status with no code of ours, such as 405, which keeps the platform's default problem document.
    /// </summary>
    public static string? CodeForStatus(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        _ => null,
    };

    /// <summary>
    /// Completes a problem document the platform is about to write (the callback of <c>AddProblemDetails</c>): the machine
    /// readable <c>errorCode</c> and its <c>type</c> for a status we own, and the trace id. The platform's default
    /// <c>traceId</c> is <c>Activity.Id</c>, the full <c>traceparent</c> string whose span part differs on every hop, so it is
    /// replaced with the bare W3C trace id (research R-005). Documents built by <see cref="ApiProblems"/> already carry the
    /// code and go through the same method.
    /// </summary>
    public static void Customize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Complete(context.ProblemDetails);
    }

    internal static void Complete(Microsoft.AspNetCore.Mvc.ProblemDetails problem)
    {
        if (!problem.Extensions.TryGetValue("errorCode", out var existing) || existing is null)
        {
            var code = CodeForStatus(problem.Status ?? 0);
            if (code is not null)
            {
                problem.Extensions["errorCode"] = code;
            }
        }

        if (problem.Extensions.TryGetValue("errorCode", out var errorCode) && errorCode?.ToString() is { Length: > 0 } text)
        {
            problem.Type = TypeFor(text);
        }

        problem.Extensions["traceId"] = TraceIds.Current();
    }
}
