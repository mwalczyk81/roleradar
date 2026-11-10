using RoleRadar.Application.Abstractions;

namespace RoleRadar.Api.Observability;

/// <summary>
/// The API's <see cref="IRequestContext"/> (feature 013, plan D6): the correlation id that
/// <see cref="CorrelationMiddleware"/> accepted or created for the current request. Scoped.
/// </summary>
public sealed class HttpRequestContext(IHttpContextAccessor httpContextAccessor) : IRequestContext
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    public string? CorrelationId => CorrelationMiddleware.Current(_httpContextAccessor.HttpContext);
}
