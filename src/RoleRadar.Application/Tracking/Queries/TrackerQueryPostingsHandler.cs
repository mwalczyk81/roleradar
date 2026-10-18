using RoleRadar.Application.Abstractions;
using RoleRadar.Application.Tracking.Commands;
using RoleRadar.Domain.Common;

namespace RoleRadar.Application.Tracking.Queries;

public sealed record TrackerQueryResultDto(
    string Entity,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<object> Items);

public sealed record TrackerQueryRequest(
    string Entity,
    Guid? PostingId = null,
    Guid? CompanyId = null,
    string? Disposition = null,
    string? Status = null,
    string? Tag = null,
    string? Name = null,
    bool IncludeRetired = false,
    bool IncludeStale = false,
    int StaleAfterDays = 7,
    string? EffectiveDate = null,
    string? DedupKey = null,
    int Page = 1,
    int PageSize = 50,
    string? OwnerId = null) : IQuery<TrackerQueryResultDto>;

public sealed class TrackerQueryHandler : IQueryHandler<TrackerQueryRequest, TrackerQueryResultDto>
{
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public TrackerQueryHandler(ITrackingTransactionContext context, IUtcClock clock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<Result<TrackerQueryResultDto>> HandleAsync(
        TrackerQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        // Stub for test-first phase
        return Task.FromResult(Result<TrackerQueryResultDto>.Failure(
            new ResultError(ErrorCode.ValidationError, "TrackerQueryHandler not yet implemented.")));
    }
}
