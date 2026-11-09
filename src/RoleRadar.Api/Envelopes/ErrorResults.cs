using RoleRadar.Api.Observability;

namespace RoleRadar.Api.Envelopes;

/// <summary>
/// Reads the current request's correlation id for the error envelope (feature 013). Uses the framework's
/// <see cref="HttpContextAccessor"/> (registered in Program), whose current context flows with the request, so an
/// envelope created anywhere during a request carries the id that the response header carries.
/// </summary>
public static class ErrorResults
{
    private static readonly HttpContextAccessor Accessor = new();

    /// <summary>The current request's id, or an empty string outside a request.</summary>
    public static string CurrentCorrelationId() => CorrelationMiddleware.Current(Accessor.HttpContext) ?? string.Empty;
}
