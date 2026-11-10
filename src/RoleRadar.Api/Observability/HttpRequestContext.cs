using RoleRadar.Application.Abstractions;
using RoleRadar.Observability;

namespace RoleRadar.Api.Observability;

/// <summary>
/// The API's <see cref="IRequestContext"/> (feature 013, plan D6; feature 014): the trace id of the current request, which
/// ASP.NET Core took from a valid <c>traceparent</c> or generated. <c>IRequestContext.CorrelationId</c> keeps its name and now
/// carries the trace id; the audit field it fills keeps its stored name <c>request_correlation_id</c> (ADR 0044). Scoped.
/// </summary>
public sealed class HttpRequestContext : IRequestContext
{
    // Scoped, so one request keeps one id even in the unlikely case that it has no activity and an id had to be generated.
    private string? _traceId;

    public string? CorrelationId => _traceId ??= TraceIds.Current();
}
