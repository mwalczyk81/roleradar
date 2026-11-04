using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Reading;
using RoleRadar.Domain.Common;

namespace RoleRadar.Application.Insights;

/// <summary>Asks for the Insights stats of one owner for a window pinned to an as-of date (UTC).</summary>
public sealed record GetInsightsStatsQuery(string OwnerId, InsightsWindow Window, DateOnly AsOf) : IQuery<InsightsStatsDto>;

/// <summary>
/// Read-only handler for GET /api/v1/insights/stats (ADR 0030). It projects two date columns from applications and two
/// from search runs through <see cref="IOwnerReadSeam"/> (never the collection) and computes in memory. It is a query, so
/// it is not wrapped by the transaction and audit decorator: it writes nothing.
/// </summary>
public sealed class GetInsightsStatsQueryHandler : IQueryHandler<GetInsightsStatsQuery, InsightsStatsDto>
{
    /// <summary>Hard cap on projected rows per source. Above it the handler fails with a typed result rather than truncating.</summary>
    public const int RowCap = 50_000;

    private readonly IOwnerReadSeam _seam;
    private readonly IUtcClock _clock;

    public GetInsightsStatsQueryHandler(IOwnerReadSeam seam, IUtcClock clock)
    {
        _seam = seam ?? throw new ArgumentNullException(nameof(seam));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<Result<InsightsStatsDto>> HandleAsync(GetInsightsStatsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var today = DateOnly.FromDateTime(_clock.UtcNow);
        if (query.AsOf > today.AddDays(1))
        {
            return Task.FromResult(Result<InsightsStatsDto>.Failure(
                ErrorCode.ValidationError,
                "asOf cannot be more than 1 day in the future."));
        }

        var owner = new OwnerId(query.OwnerId);
        var bounds = InsightsStatsCalculator.Bounds(query.Window, query.AsOf);

        // The window is applied in the query, so the cap bounds what is actually read. One extra row detects an overflow.
        var applicationsInWindow = _seam.QueryApplications(owner).Where(a => a.AppliedAt < bounds.EndExclusive);
        if (bounds.Start is { } start)
        {
            applicationsInWindow = applicationsInWindow.Where(a => a.AppliedAt >= start);
        }

        var applications = applicationsInWindow
            .Select(a => new { a.AppliedAt, a.FirstResponseAt })
            .Take(RowCap + 1)
            .ToList();
        if (applications.Count > RowCap)
        {
            return Task.FromResult(VolumeLimit("applications"));
        }

        var runsInWindow = _seam.QuerySearchRuns(owner).Where(r => r.RunDate < bounds.EndExclusive);
        if (bounds.Start is { } runStart)
        {
            runsInWindow = runsInWindow.Where(r => r.RunDate >= runStart);
        }

        var runs = runsInWindow
            .Select(r => new { r.RunDate, r.NewPostings })
            .Take(RowCap + 1)
            .ToList();
        if (runs.Count > RowCap)
        {
            return Task.FromResult(VolumeLimit("search runs"));
        }

        var dto = new InsightsStatsDto(
            AsOf: query.AsOf,
            Window: InsightsWindows.ToWireName(query.Window),
            WindowStart: bounds.Start is null ? null : DateOnly.FromDateTime(bounds.Start.Value.UtcDateTime),
            ResponseTime: InsightsStatsCalculator.ResponseTime(
                applications.Select(a => (a.AppliedAt, a.FirstResponseAt)), query.Window, query.AsOf),
            WeeklyApplications: InsightsStatsCalculator.WeeklyApplications(
                applications.Select(a => a.AppliedAt), query.Window, query.AsOf),
            SearchRunYield: InsightsStatsCalculator.RunYield(
                runs.Select(r => (r.RunDate, r.NewPostings)), query.Window, query.AsOf));

        return Task.FromResult(Result<InsightsStatsDto>.Success(dto));
    }

    private static Result<InsightsStatsDto> VolumeLimit(string source) =>
        Result<InsightsStatsDto>.Failure(
            ErrorCode.ValidationError,
            $"The Insights stats read reached its volume limit of 50,000 {source} for this window. Narrow the window.");
}
