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
    public static ErrorEnvelope Validation(string message, IReadOnlyList<ErrorFieldDetail>? details = null) =>
        new(false, "validation_error", message, details);

    public static ErrorEnvelope NotFound(string message) =>
        new(false, "not_found", message);

    public static ErrorEnvelope ConcurrencyConflict(string message) =>
        new(false, "concurrency_conflict", message);

    public static IResult FromResultError(ResultError? error)
    {
        if (error is null)
        {
            return Results.BadRequest(Validation("The request payload failed validation."));
        }

        return error.Value.Code switch
        {
            RoleRadar.Application.Abstractions.ErrorCode.NotFound =>
                Results.NotFound(NotFound(error.Value.Message)),
            RoleRadar.Application.Abstractions.ErrorCode.StaleVersionToken =>
                Results.Conflict(ConcurrencyConflict(error.Value.Message)),
            _ =>
                Results.BadRequest(Validation(error.Value.Message))
        };
    }
}
