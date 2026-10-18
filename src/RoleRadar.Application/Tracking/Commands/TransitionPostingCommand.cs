using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
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

public sealed class TransitionPostingCommandValidator : ICommandValidator<TransitionPostingCommand>
{
    public ResultError? Validate(TransitionPostingCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PostingId == Guid.Empty)
        {
            return new ResultError(ErrorCode.ValidationError, "PostingId is required.");
        }

        if (string.IsNullOrWhiteSpace(command.TargetDisposition))
        {
            return new ResultError(ErrorCode.ValidationError, "TargetDisposition is required.");
        }

        if (command.VersionToken == Guid.Empty)
        {
            return new ResultError(ErrorCode.StaleVersionToken, "VersionToken is required.");
        }

        if (string.Equals(command.TargetDisposition, "applied", StringComparison.OrdinalIgnoreCase))
        {
            return new ResultError(ErrorCode.InvalidTransition, "Target 'applied' is disallowed via posting_transition; applications must be created via application_record.");
        }

        if (!TrackingEnumExtensions.TryParseDisposition(command.TargetDisposition, out var targetDisp))
        {
            return new ResultError(ErrorCode.ValidationError, $"Unknown disposition '{command.TargetDisposition}'.");
        }

        if (targetDisp is PostingDisposition.ScreenedOut or PostingDisposition.Closed or PostingDisposition.Rejected)
        {
            if (string.IsNullOrWhiteSpace(command.Reason))
            {
                return new ResultError(ErrorCode.ValidationError, $"Reason is required when transitioning to '{command.TargetDisposition}'.");
            }

            if (command.Reason.Trim().Length > Posting.MaxDispositionReasonLength)
            {
                return new ResultError(ErrorCode.ValidationError, $"Reason cannot exceed {Posting.MaxDispositionReasonLength} characters.");
            }
        }

        return null;
    }
}

public sealed class TransitionPostingCommandHandler : ICommandHandler<TransitionPostingCommand, PostingTransitionResultDto>
{
    private static readonly TransitionPostingCommandValidator Validator = new();
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public TransitionPostingCommandHandler(ITrackingTransactionContext context, IUtcClock clock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<PostingTransitionResultDto>> HandleAsync(
        TransitionPostingCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var valError = Validator.Validate(command);
        if (valError is not null)
        {
            return Result<PostingTransitionResultDto>.Failure(valError.Value);
        }

        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;
        var now = _clock.UtcNow;

        var postingResult = await _context.Postings.GetByIdAsync(ownerId, command.PostingId, cancellationToken);
        if (!postingResult.IsSuccess || postingResult.Value is null)
        {
            return Result<PostingTransitionResultDto>.Failure(ErrorCode.NotFound, "Posting not found.");
        }

        var posting = postingResult.Value;
        if (posting.VersionToken != command.VersionToken)
        {
            return Result<PostingTransitionResultDto>.Failure(ErrorCode.StaleVersionToken, "Posting version token mismatch or stale.");
        }

        if (!TrackingEnumExtensions.TryParseDisposition(command.TargetDisposition, out var targetDisposition))
        {
            return Result<PostingTransitionResultDto>.Failure(ErrorCode.ValidationError, $"Unknown disposition '{command.TargetDisposition}'.");
        }

        // Disallow target applied
        if (targetDisposition == PostingDisposition.Applied)
        {
            return Result<PostingTransitionResultDto>.Failure(ErrorCode.InvalidTransition, "Target 'applied' is unreachable via posting_transition.");
        }

        // Validate via DispositionMatrix
        if (!DispositionMatrix.CanTransition(posting.Disposition, targetDisposition, posting.ResurfacePolicy))
        {
            return Result<PostingTransitionResultDto>.Failure(
                ErrorCode.InvalidTransition,
                $"Invalid state transition from '{posting.Disposition.ToSnakeCase()}' to '{targetDisposition.ToSnakeCase()}' with policy '{posting.ResurfacePolicy.ToSnakeCase()}'.");
        }

        // Reopening rejected requires explicit override reason
        if (posting.Disposition == PostingDisposition.Rejected && targetDisposition == PostingDisposition.Lead)
        {
            if (string.IsNullOrWhiteSpace(command.OverrideReason))
            {
                return Result<PostingTransitionResultDto>.Failure(
                    ErrorCode.ValidationError,
                    "Reopening a rejected posting requires an explicit non-empty overrideReason.");
            }
        }

        var resurfacePolicy = posting.ResurfacePolicy;
        if (!string.IsNullOrWhiteSpace(command.ResurfacePolicy) &&
            TrackingEnumExtensions.TryParseResurfacePolicy(command.ResurfacePolicy, out var parsedRp))
        {
            resurfacePolicy = parsedRp;
        }

        var prevDisposition = posting.Disposition;
        var reason = command.Reason ?? command.OverrideReason;

        posting.Transition(targetDisposition, reason, command.CriterionId, resurfacePolicy, now);
        await _context.Postings.UpsertPostingAsync(posting, cancellationToken);

        var auditSummary = new PostingAuditSummary(
            PostingId: posting.Id,
            ChangedFieldNames: ["disposition", "disposition_reason", "resurface_policy", "decided_at"],
            PriorDisposition: prevDisposition.ToSnakeCase(),
            NewDisposition: targetDisposition.ToSnakeCase(),
            Reason: reason,
            Timestamp: now);

        await AppendAuditAsync(ownerId, command.Actor, "posting_transition", posting.Id, posting.VersionToken, auditSummary, cancellationToken);

        return Result<PostingTransitionResultDto>.Success(new PostingTransitionResultDto(
            PostingId: posting.Id,
            PreviousDisposition: prevDisposition.ToSnakeCase(),
            CurrentDisposition: targetDisposition.ToSnakeCase(),
            DispositionReason: posting.DispositionReason,
            CriterionId: posting.DispositionCriterionId,
            ResurfacePolicy: posting.ResurfacePolicy.ToSnakeCase(),
            DecidedAtUtc: posting.DecidedAt,
            VersionToken: posting.VersionToken,
            UpdatedAtUtc: posting.UpdatedAt));
    }

    private Task AppendAuditAsync(
        string ownerId,
        string actor,
        string operation,
        Guid postingId,
        Guid versionToken,
        PostingAuditSummary summary,
        CancellationToken cancellationToken)
    {
        var beforeSummary = new AuditContentSummary(
            bodyHash: "0000000000000000000000000000000000000000000000000000000000000000",
            byteLength: 0,
            status: "prior",
            excerpt: summary.PriorDisposition ?? string.Empty);

        var entry = AuditLogEntry.Create(
            ownerId: ownerId,
            actor: actor,
            operation: AuditOperation.NoteAppend,
            targetNoteId: postingId,
            noteRevisionId: null,
            appendedText: $"{summary.PriorDisposition}->{summary.NewDisposition}",
            resultingVersionToken: versionToken,
            resolvedDefaultDate: null,
            occurredAtUtc: summary.Timestamp.UtcDateTime,
            beforeSummary: beforeSummary,
            afterSummary: new AuditContentSummary(
                bodyHash: "0000000000000000000000000000000000000000000000000000000000000000",
                byteLength: 4,
                status: operation,
                excerpt: summary.Reason ?? $"{summary.PriorDisposition}->{summary.NewDisposition}"));

        return _context.AppendAuditEntryAsync(entry, cancellationToken);
    }
}
