using Microsoft.Extensions.Logging;

namespace RoleRadar.Observability;

/// <summary>
/// Sign-in security events (feature 013, FR-018, FR-019, ADR 0041). Logs only: nothing is stored. Each event carries
/// the correlation id of the request from the logging scope and <c>subject</c>, the matched account's id. No method
/// takes a username, password or one-time code, so none of them can reach a log line.
/// </summary>
public static partial class SecurityEvents
{
    [LoggerMessage(EventId = 5001, EventName = "SignInSucceeded", Level = LogLevel.Information, Message = "Sign-in succeeded for subject {subject}")]
    public static partial void SignInSucceeded(ILogger logger, string subject);

    [LoggerMessage(EventId = 5002, EventName = "SignInFailed", Level = LogLevel.Warning, Message = "Sign-in failed for subject {subject}")]
    public static partial void SignInFailed(ILogger logger, string subject);

    [LoggerMessage(EventId = 5003, EventName = "AccountLockedOut", Level = LogLevel.Warning, Message = "Account locked out, subject {subject}")]
    public static partial void AccountLockedOut(ILogger logger, string subject);

    [LoggerMessage(EventId = 5004, EventName = "OneTimeCodeFailed", Level = LogLevel.Warning, Message = "One-time code failed for subject {subject}")]
    public static partial void OneTimeCodeFailed(ILogger logger, string subject);
}

/// <summary>
/// The failed sign-in event for an attempt that matched no account. It has the same id and name as
/// <see cref="SecurityEvents.SignInFailed"/>, but no <c>subject</c> property at all, not even a null one. It is a
/// separate class only because the logging source generator rejects two methods with one event id in the same class.
/// </summary>
public static partial class UnmatchedAccountEvents
{
    [LoggerMessage(EventId = 5002, EventName = "SignInFailed", Level = LogLevel.Warning, Message = "Sign-in failed, no account matched")]
    public static partial void SignInFailed(ILogger logger);
}
