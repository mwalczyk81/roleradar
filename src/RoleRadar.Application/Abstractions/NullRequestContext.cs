namespace RoleRadar.Application.Abstractions;

/// <summary>The default <see cref="IRequestContext"/>: no request, so audit entries get no request correlation id.</summary>
internal sealed class NullRequestContext : IRequestContext
{
    public string? CorrelationId => null;
}
