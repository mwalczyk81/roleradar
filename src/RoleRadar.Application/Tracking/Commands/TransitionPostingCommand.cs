using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Commands;

public sealed record PostingTransitionResultDto(
    Guid PostingId,
    string PreviousDisposition,
    string CurrentDisposition,
    string? DispositionReason,
    Guid? CriterionId,
    string ResurfacePolicy,
    DateTimeOffset? DecidedAtUtc,
    Guid VersionToken,
    DateTimeOffset UpdatedAtUtc);

public sealed record TransitionPostingCommand(
    Guid PostingId,
    string TargetDisposition,
    Guid VersionToken,
    string? Reason = null,
    Guid? CriterionId = null,
    string? ResurfacePolicy = null,
    string? OverrideReason = null,
    string Actor = "mcp-stdio",
    string? OwnerId = null) : ICommand<PostingTransitionResultDto>;

public sealed class TransitionPostingCommandHandler : ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto>
{
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public TransitionPostingCommandHandler(ITrackingTransactionContext context, IUtcClock clock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task<Result<PostingTransitionResultDto>> HandleAsync(
        TransitionPostingCommand command,
        CancellationToken cancellationToken = default)
    {
        // Stub for test-first phase
        return Task.FromResult(Result<PostingTransitionResultDto>.Failure(
            new ResultError(ErrorCode.ValidationError, "TransitionPostingCommandHandler not yet implemented.")));
    }
}
