using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Commands;

public sealed record PostingDto(
    Guid Id,
    Guid CompanyId,
    string Title,
    string NormalizedTitle,
    string Disposition,
    string ResurfacePolicy,
    Guid VersionToken,
    string? Location = null,
    string? RemoteType = null,
    decimal? Score = null,
    string? CultureLevel = null,
    DateTimeOffset? LastVerifiedAt = null);

public sealed record SignalInputDto(
    string SignalType,
    string Evidence,
    string? ObservedAt = null,
    string? DeadlineDate = null);

public sealed record PostingRecordResultDto(
    string Status,
    PostingDto? Posting = null,
    string? PriorReason = null,
    bool? CanReopen = null,
    IReadOnlyList<PostingCandidate>? Candidates = null,
    IReadOnlyList<PostingDifference>? Differences = null);

public sealed record RecordPostingCommand(
    Guid? PostingId = null,
    Guid? VersionToken = null,
    Guid? CompanyId = null,
    string? Title = null,
    string? Url = null,
    string? SourceKind = null,
    string? AtsTenant = null,
    string? RequisitionId = null,
    string? Location = null,
    string? RemoteType = null,
    decimal? CompensationMin = null,
    decimal? CompensationMax = null,
    string? CompensationCurrency = null,
    string? CompensationText = null,
    string? PostedDate = null,
    decimal? Score = null,
    string? CultureLevel = null,
    string? CultureSummary = null,
    string? ResurfacePolicy = null,
    Guid? ScreeningNoteId = null,
    Guid? DuplicateOfId = null,
    IReadOnlyList<SignalInputDto>? Signals = null,
    string Actor = "mcp-stdio",
    string? OwnerId = null) : ICommand<PostingRecordResultDto>;

public sealed class RecordPostingCommandHandler : ICommandHandler<RecordPostingCommand, PostingRecordResultDto>
{
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public RecordPostingCommandHandler(ITrackingTransactionContext context, IUtcClock clock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<Result<PostingRecordResultDto>> HandleAsync(
        RecordPostingCommand command,
        CancellationToken cancellationToken = default)
    {
        // Stub for test-first phase
        return Task.FromResult(Result<PostingRecordResultDto>.Failure(
            new ResultError(ErrorCode.ValidationError, "RecordPostingCommandHandler not yet implemented.")));
    }
}
