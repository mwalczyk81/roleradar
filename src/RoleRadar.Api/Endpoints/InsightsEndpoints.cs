using System.Globalization;
using RoleRadar.Api.Envelopes;
using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Insights;
using RoleRadar.Domain.Common;

namespace RoleRadar.Api.Endpoints;

/// <summary>
/// The one Insights endpoint: <c>GET /api/v1/insights/stats?window=&amp;asOf=</c> (feature 007, ADR 0030). Read only, owner
/// scoped through <see cref="IOwnerIdProvider"/>, answered by an Application query handler; this class touches no
/// storage type. Only GET is mapped, so any other method is answered 405 with <c>Allow: GET</c> by routing.
/// </summary>
public static class InsightsEndpoints
{
    public const string StatsPath = "/insights/stats";

    public static RouteGroupBuilder MapInsightsEndpoints(this RouteGroupBuilder group)
    {
        var readGroup = group.MapGroup(string.Empty)
            .RequireAuthorization(Program.ReadPolicyName);

        readGroup.MapGet(StatsPath, GetStatsAsync)
            .WithName("GetInsightsStats")
            .Produces<InsightsStatsDto>(StatusCodes.Status200OK)
            .Produces<ErrorEnvelope>(StatusCodes.Status400BadRequest);
        return group;
    }

    private static async Task<IResult> GetStatsAsync(
        string? window,
        string? asOf,
        IOwnerIdProvider ownerIdProvider,
        IQueryHandler<GetInsightsStatsQuery, InsightsStatsDto> queryHandler,
        CancellationToken cancellationToken)
    {
        var details = new List<ErrorFieldDetail>();

        if (!InsightsWindows.TryParse(window, out var parsedWindow))
        {
            details.Add(new ErrorFieldDetail("window", "window is required and must be one of: " + string.Join(", ", InsightsWindows.Names) + "."));
        }

        // Exactly yyyy-MM-dd: no time part, no short forms, no locale.
        if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedAsOf))
        {
            details.Add(new ErrorFieldDetail("asOf", "asOf is required and must be a date written as yyyy-MM-dd."));
        }

        if (details.Count > 0)
        {
            return Results.BadRequest(ErrorEnvelope.Validation("The window or as-of date is missing or invalid.", details));
        }

        var ownerId = ownerIdProvider.GetOwnerId().Value;
        var result = await queryHandler.HandleAsync(new GetInsightsStatsQuery(ownerId, parsedWindow, parsedAsOf), cancellationToken);
        if (!result.IsSuccess || result.Value is null)
        {
            return ErrorEnvelope.FromResultError(result.Error);
        }

        return Results.Ok(result.Value);
    }
}
