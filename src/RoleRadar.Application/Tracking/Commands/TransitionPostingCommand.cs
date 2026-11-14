using System.Globalization;
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
    DateTimeOffset UpdatedAtUtc)
{
    /// <summary>Non-fatal notices about the application linked to the posting. Empty when there are none.</summary>
    public IReadOnlyList<TransitionWarningDto> Warnings { get; init; } = [];
}

/// <summary>A fixed, host-displayable notice. Hosts show <see cref="Message"/> and never branch on <see cref="Code"/>.</summary>
public sealed record TransitionWarningDto(string Code, string Message);

/// <summary>The closed set of transition warnings (ADR 0033). Messages are fixed strings, never posting text.</summary>
public static class TransitionWarnings
{
    public const string ApplicationStillRejected = "application_still_rejected";
    public const string ApplicationClosed = "application_closed";

    public static IReadOnlyList<string> Codes { get; } = [ApplicationStillRejected, ApplicationClosed];

    public static TransitionWarningDto Create(string code) => code switch
    {
        ApplicationStillRejected => new TransitionWarningDto(code, "The application is still rejected."),
        ApplicationClosed => new TransitionWarningDto(code, "The application is closed and was not changed."),
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown transition warning code."),
    };
}

public sealed record TransitionPostingCommand(
    Guid PostingId,
    string TargetDisposition,
    Guid VersionToken,
    string Actor,
    string? Reason = null,
    Guid? CriterionId = null,
    string? ResurfacePolicy = null,
    string? OverrideReason = null,
    string? EventDate = null,
    string? OwnerId = null) : ICommand<PostingTransitionResultDto>;

public sealed class TransitionPostingCommandValidator : ICommandValidator<TransitionPostingCommand>
{
    public ResultError? Validate(TransitionPostingCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var actorError = CommandActor.Validate(command.Actor);
        if (actorError is not null)
        {
            return actorError;
        }

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

        if (!TrackingEnumExtensions.TryParseDisposition(command.TargetDisposition, out var targetDisp))
        {
            return new ResultError(ErrorCode.ValidationError, $"Unknown disposition '{command.TargetDisposition}'.");
        }

        // Checked on the parsed value, not the raw string, so every spelling the parser accepts (case,
        // surrounding whitespace, hyphens) is rejected here, in one place.
        if (targetDisp == PostingDisposition.Applied)
        {
            return new ResultError(ErrorCode.InvalidTransition, "Target 'applied' is disallowed via posting_transition; applications must be created via application_record.");
        }

        // EventDate only matters when the transition records a rejection on the application. Rejected is
        // reachable only from applied, so validating it for the rejected target is exact, and it is ignored
        // (never validated) for every other target.
        if (targetDisp == PostingDisposition.Rejected
            && !string.IsNullOrWhiteSpace(command.EventDate)
            && !DateOnly.TryParseExact(command.EventDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return new ResultError(
                ErrorCode.ValidationError,
                $"Event date must be in YYYY-MM-DD format: '{command.EventDate}'.",
                new Dictionary<string, object?> { ["field"] = "eventDate" });
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
    /// <summary>Event source recorded on the application event created by a posting transition (ADR 0033).</summary>
    public const string PostingTransitionEventSource = "posting_transition";

    // The decorator runs the registered validator on the normal path. The handler keeps this call so a caller
    // that constructs it directly (the contract tests do) cannot skip validation.
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

        // Target 'applied' is rejected by the validator above, so it never reaches the matrix.

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

        // Read everything the application side needs before any write, so a failure here changes nothing.
        // Structural no-loop guarantee: this handler depends only on the transaction context and the clock. It
        // writes the application through the repository pair below and has no path to the application-event
        // handler that syncs an application event back onto the posting.
        var planResult = await PlanApplicationSyncAsync(ownerId, posting, prevDisposition, targetDisposition, command, reason, now, cancellationToken);
        if (!planResult.IsSuccess)
        {
            return Result<PostingTransitionResultDto>.Failure(planResult.Error!.Value);
        }

        var plan = planResult.Value!;

        posting.Transition(targetDisposition, reason, command.CriterionId, resurfacePolicy, now);
        var postingWrite = await _context.Postings.UpsertPostingAsync(posting, cancellationToken);
        if (!postingWrite.IsSuccess)
        {
            return Result<PostingTransitionResultDto>.Failure(postingWrite.Error!.Value);
        }

        string? priorApplicationOutcome = null;
        if (plan.Event is not null && plan.Application is not null)
        {
            priorApplicationOutcome = plan.Application.Outcome.ToSnakeCase();
            plan.Application.ApplyEvent(ApplicationEventType.Rejected, plan.Event.Notes, now);

            // The existing repository pair: AppendEventAsync is the single place milestones and derived
            // read-model fields are recomputed.
            var appWrite = await _context.Applications.UpsertApplicationAsync(plan.Application, cancellationToken);
            if (!appWrite.IsSuccess)
            {
                return Result<PostingTransitionResultDto>.Failure(appWrite.Error!.Value);
            }

            var eventWrite = await _context.Applications.AppendEventAsync(plan.Event, cancellationToken);
            if (!eventWrite.IsSuccess)
            {
                return Result<PostingTransitionResultDto>.Failure(eventWrite.Error!.Value);
            }
        }

        var correlationId = plan.Event is not null ? Guid.CreateVersion7().ToString() : null;

        var auditSummary = new PostingAuditSummary(
            PostingId: posting.Id,
            ChangedFieldNames: ["disposition", "disposition_reason", "resurface_policy", "decided_at"],
            PriorDisposition: prevDisposition.ToSnakeCase(),
            NewDisposition: targetDisposition.ToSnakeCase(),
            Reason: reason,
            Timestamp: now);

        await AppendAuditAsync(
            ownerId,
            command.Actor,
            AuditOperation.PostingTransition,
            posting.Id,
            posting.VersionToken,
            auditSummary,
            correlationId,
            plan.Warnings.Select(w => w.Code).ToArray(),
            cancellationToken);

        if (plan.Event is not null && plan.Application is not null)
        {
            var appDetails = AuditDetails.ForEntity(
                changedFields: ["outcome"],
                before: new Dictionary<string, object?> { ["outcome"] = priorApplicationOutcome },
                after: new Dictionary<string, object?>
                {
                    ["outcome"] = plan.Application.Outcome.ToSnakeCase(),
                    ["event_type"] = plan.Event.EventType.ToSnakeCase(),
                    ["event_date"] = plan.Event.EventDate,
                    ["event_source"] = plan.Event.EventSource,
                    ["correlation_id"] = correlationId,
                });

            await _context.AppendAuditEntryAsync(
                AuditLogEntry.CreateForTracking(
                    ownerId: ownerId,
                    actor: command.Actor,
                    operation: AuditOperation.ApplicationEventAppend,
                    entityType: "application",
                    entityId: plan.Application.Id,
                    resultingVersionToken: plan.Application.VersionToken,
                    occurredAtUtc: now,
                    details: appDetails),
                cancellationToken);
        }

        return Result<PostingTransitionResultDto>.Success(new PostingTransitionResultDto(
            PostingId: posting.Id,
            PreviousDisposition: prevDisposition.ToSnakeCase(),
            CurrentDisposition: targetDisposition.ToSnakeCase(),
            DispositionReason: posting.DispositionReason,
            CriterionId: posting.DispositionCriterionId,
            ResurfacePolicy: posting.ResurfacePolicy.ToSnakeCase(),
            DecidedAtUtc: posting.DecidedAt,
            VersionToken: posting.VersionToken,
            UpdatedAtUtc: posting.UpdatedAt)
        {
            Warnings = plan.Warnings,
        });
    }

    private sealed record ApplicationSyncPlan(
        Domain.Tracking.Application? Application,
        ApplicationEvent? Event,
        IReadOnlyList<TransitionWarningDto> Warnings)
    {
        public static readonly ApplicationSyncPlan None = new(null, null, []);
    }

    /// <summary>
    /// Decides, from reads only, whether the transition must record a rejected event on the posting's
    /// application and which warnings apply. Rows of the table in research R3.
    /// </summary>
    private async Task<Result<ApplicationSyncPlan>> PlanApplicationSyncAsync(
        string ownerId,
        Posting posting,
        PostingDisposition previous,
        PostingDisposition target,
        TransitionPostingCommand command,
        string? reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var isReject = previous == PostingDisposition.Applied && target == PostingDisposition.Rejected;
        var isReopen = previous == PostingDisposition.Rejected && target == PostingDisposition.Lead;
        if (!isReject && !isReopen)
        {
            return Result<ApplicationSyncPlan>.Success(ApplicationSyncPlan.None);
        }

        var appRes = await _context.Applications.GetByPostingIdAsync(ownerId, posting.Id, cancellationToken);
        if (!appRes.IsSuccess)
        {
            return Result<ApplicationSyncPlan>.Failure(appRes.Error!.Value);
        }

        var application = appRes.Value;
        if (application is null)
        {
            // A posting with no application behaves exactly as before.
            return Result<ApplicationSyncPlan>.Success(ApplicationSyncPlan.None);
        }

        if (isReopen)
        {
            return Result<ApplicationSyncPlan>.Success(application.Outcome == ApplicationOutcome.Rejected
                ? new ApplicationSyncPlan(null, null, [TransitionWarnings.Create(TransitionWarnings.ApplicationStillRejected)])
                : ApplicationSyncPlan.None);
        }

        switch (application.Outcome)
        {
            case ApplicationOutcome.Rejected:
                return Result<ApplicationSyncPlan>.Success(ApplicationSyncPlan.None);

            case ApplicationOutcome.Closed:
                return Result<ApplicationSyncPlan>.Success(
                    new ApplicationSyncPlan(null, null, [TransitionWarnings.Create(TransitionWarnings.ApplicationClosed)]));
        }

        var eventsRes = await _context.Applications.GetEventsAsync(ownerId, application.Id, cancellationToken);
        if (!eventsRes.IsSuccess)
        {
            return Result<ApplicationSyncPlan>.Failure(eventsRes.Error!.Value);
        }

        if (eventsRes.Value!.Any(e => e.EventType == ApplicationEventType.Rejected))
        {
            return Result<ApplicationSyncPlan>.Success(ApplicationSyncPlan.None);
        }

        var eventDate = string.IsNullOrWhiteSpace(command.EventDate)
            ? DateOnly.FromDateTime(now.UtcDateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : command.EventDate.Trim();

        ApplicationEvent appEvent;
        try
        {
            appEvent = ApplicationEvent.Create(
                ownerId: ownerId,
                applicationId: application.Id,
                eventDate: eventDate,
                eventType: ApplicationEventType.Rejected,
                eventSource: PostingTransitionEventSource,
                notes: reason,
                now: now);
        }
        catch (ArgumentException ex)
        {
            return Result<ApplicationSyncPlan>.Failure(ErrorCode.ValidationError, ex.Message);
        }

        return Result<ApplicationSyncPlan>.Success(new ApplicationSyncPlan(application, appEvent, []));
    }

    private Task AppendAuditAsync(
        string ownerId,
        string actor,
        string operation,
        Guid postingId,
        Guid versionToken,
        PostingAuditSummary summary,
        string? correlationId,
        string[] warningCodes,
        CancellationToken cancellationToken)
    {
        var after = new Dictionary<string, object?> { ["disposition"] = summary.NewDisposition, ["reason"] = summary.Reason };
        if (correlationId is not null)
        {
            after["correlation_id"] = correlationId;
        }

        if (warningCodes.Length > 0)
        {
            after["warnings"] = warningCodes;
        }

        var details = AuditDetails.ForEntity(
            changedFields: summary.ChangedFieldNames,
            before: new Dictionary<string, object?> { ["disposition"] = summary.PriorDisposition },
            after: after);

        var entry = AuditLogEntry.CreateForTracking(
            ownerId: ownerId,
            actor: actor,
            operation: operation,
            entityType: "posting",
            entityId: postingId,
            resultingVersionToken: versionToken,
            occurredAtUtc: summary.Timestamp.UtcDateTime,
            details: details);

        return _context.AppendAuditEntryAsync(entry, cancellationToken);
    }
}
