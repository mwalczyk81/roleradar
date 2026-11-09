namespace RoleRadar.Application.Abstractions;

/// <summary>
/// The correlation id of the request, tool call or CLI run that is currently writing (feature 013, ADR 0041, plan D6).
/// Scoped. Read only by the audit choke points in Infrastructure; the hosts set it.
/// </summary>
public interface IRequestContext
{
    string? CorrelationId { get; }
}
