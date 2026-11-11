using System.Diagnostics;

namespace RoleRadar.Observability;

/// <summary>
/// The one id every RoleRadar host uses to follow a request, call or run (feature 014, ADR 0044): the bare W3C trace id, 32
/// lower-case hexadecimal characters and not all zero. It replaces the 013 correlation id and its header.
/// <para>
/// ASP.NET Core extracts <c>traceparent</c> and starts the request's <see cref="Activity"/> itself, and <see cref="HttpClient"/>
/// propagates it, so nothing here parses a header. A request with no trace context gets a generated trace id from the platform.
/// The CLI and the MCP host have no request, so they start an activity per run or call with <see cref="StartActivity"/>.
/// </para>
/// </summary>
public static class TraceIds
{
    public const string ActivitySourceName = "RoleRadar";

    private static readonly ActivitySource Source = new(ActivitySourceName);

    /// <summary>
    /// The trace id of <see cref="Activity.Current"/>, or null when there is none (startup and shutdown records carry no id).
    /// This is the bare trace id, never <see cref="Activity.Id"/> (the full <c>traceparent</c> string, whose span part differs on
    /// every hop).
    /// </summary>
    public static string? CurrentOrNull() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity ? activity.TraceId.ToHexString() : null;

    /// <summary>
    /// The trace id of the current activity, or a freshly generated one when there is no activity, so no audit entry is ever
    /// without an id. Two calls with no activity return different ids: start an activity when several writes must share one.
    /// </summary>
    public static string Current() => CurrentOrNull() ?? NewRunId();

    /// <summary>A new random trace id: <see cref="ActivityTraceId.CreateRandom"/> as 32 lower-case hex characters.</summary>
    public static string NewRunId() => ActivityTraceId.CreateRandom().ToHexString();

    /// <summary>
    /// Starts an activity (W3C format) and makes it current, so every log record written inside it carries its trace id. With a
    /// <paramref name="traceId"/> the activity joins that trace; without one it begins a new trace. The caller disposes it.
    /// </summary>
    public static Activity StartActivity(string name, string? traceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (traceId is null)
        {
            var activity = Source.StartActivity(name, ActivityKind.Internal, parentContext: default);
            if (activity is not null)
            {
                return activity;
            }

            var fallback = new Activity(name);
            fallback.SetIdFormat(ActivityIdFormat.W3C);
            fallback.ActivityTraceFlags = ActivityTraceFlags.Recorded;
            return fallback.Start();
        }
        else
        {
            var parentContext = new ActivityContext(
                ActivityTraceId.CreateFromString(traceId),
                ActivitySpanId.CreateRandom(),
                ActivityTraceFlags.Recorded);

            var activity = Source.StartActivity(name, ActivityKind.Internal, parentContext);
            if (activity is not null)
            {
                return activity;
            }

            var fallback = new Activity(name);
            fallback.SetParentId(parentContext.TraceId, parentContext.SpanId, ActivityTraceFlags.Recorded);
            return fallback.Start();
        }
    }
}
