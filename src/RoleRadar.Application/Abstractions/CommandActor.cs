namespace RoleRadar.Application.Abstractions;

/// <summary>
/// Feature 013 (ADR 0041, plan D5): every command names its actor. A blank actor is an expected failure, returned as a
/// <see cref="ErrorCode.ValidationError"/> result, never thrown and never replaced by a default.
/// </summary>
public static class CommandActor
{
    public static ResultError? Validate(string? actor) =>
        string.IsNullOrWhiteSpace(actor)
            ? new ResultError(ErrorCode.ValidationError, "Actor is required and cannot be blank.")
            : null;
}
