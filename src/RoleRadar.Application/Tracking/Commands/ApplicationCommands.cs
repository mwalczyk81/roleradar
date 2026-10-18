using System.Globalization;
using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;
using ApplicationEntity = RoleRadar.Domain.Tracking.Application;

namespace RoleRadar.Application.Tracking.Commands;

public sealed record ApplicationEventInputDto(
    string EventType,
    string EventDate,
    string? EventSource = null,
    string? Notes = null);

public sealed record RecordApplicationCommand(
    Guid PostingId,
    string Action,
    string? SubmittedDate = null,
    decimal? DesiredSalaryMin = null,
    decimal? DesiredSalaryMax = null,
    string? DesiredSalaryCurrency = null,
    string? TargetStartDate = null,
    string? ResumeVariant = null,
    string? ResumePointer = null,
    Guid? ReferralContactId = null,
    string? ConfirmationCode = null,
    bool OverrideDecided = false,
    string? OverrideReason = null,
    ApplicationEventInputDto? Event = null,
    string Actor = "agent",
    string? OwnerId = null) : ICommand<ApplicationRecordResultDto>;

public sealed record ApplicationRecordResultDto(
    Guid ApplicationId,
    Guid PostingId,
    string CurrentOutcome,
    bool OverrideApplied,
    int EventCount,
    DateTimeOffset UpdatedAtUtc);

public sealed class RecordApplicationCommandValidator : ICommandValidator<RecordApplicationCommand>
{
    public ResultError? Validate(RecordApplicationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PostingId == Guid.Empty)
        {
            return new ResultError(ErrorCode.ValidationError, "Posting ID cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(command.Action))
        {
            return new ResultError(ErrorCode.ValidationError, "Action is required ('create' or 'append_event').");
        }

        var action = command.Action.Trim().ToLowerInvariant();
        if (action == "create")
        {
            if (string.IsNullOrWhiteSpace(command.SubmittedDate))
            {
                return new ResultError(ErrorCode.ValidationError, "SubmittedDate is required when action is 'create'.");
            }

            if (!DateOnly.TryParseExact(command.SubmittedDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return new ResultError(ErrorCode.ValidationError, $"SubmittedDate must be in YYYY-MM-DD format: '{command.SubmittedDate}'.");
            }

            if (string.IsNullOrWhiteSpace(command.ResumeVariant) ||
                !TrackingEnumExtensions.TryParseResumeVariant(command.ResumeVariant, out _))
            {
                return new ResultError(ErrorCode.ValidationError, "ResumeVariant is required for 'create' ('tailored', 'as_is', 'ic_framed').");
            }

            if (command.OverrideDecided && string.IsNullOrWhiteSpace(command.OverrideReason))
            {
                return new ResultError(ErrorCode.ValidationError, "Reopen-and-apply override requires an explicit non-empty overrideReason.");
            }
        }
        else if (action == "append_event")
        {
            if (command.Event is null)
            {
                return new ResultError(ErrorCode.ValidationError, "Event payload is required when action is 'append_event'.");
            }

            if (string.IsNullOrWhiteSpace(command.Event.EventType) ||
                !TrackingEnumExtensions.TryParseApplicationEventType(command.Event.EventType, out _))
            {
                return new ResultError(ErrorCode.ValidationError, $"Invalid eventType '{command.Event.EventType}'.");
            }

            if (string.IsNullOrWhiteSpace(command.Event.EventDate) ||
                !DateOnly.TryParseExact(command.Event.EventDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return new ResultError(ErrorCode.ValidationError, $"EventDate must be in YYYY-MM-DD format: '{command.Event.EventDate}'.");
            }
        }
        else
        {
            return new ResultError(ErrorCode.ValidationError, $"Unknown action '{command.Action}'. Expected 'create' or 'append_event'.");
        }

        return null;
    }
}

public sealed class RecordApplicationCommandHandler : ICommandHandler<RecordApplicationCommand, ApplicationRecordResultDto>
{
    private static readonly RecordApplicationCommandValidator Validator = new();
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public RecordApplicationCommandHandler(ITrackingTransactionContext context, IUtcClock? clock = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? SystemUtcClock.Instance;
    }

    public async Task<Result<ApplicationRecordResultDto>> HandleAsync(
        RecordApplicationCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var valError = Validator.Validate(command);
        if (valError is not null)
        {
            return Result<ApplicationRecordResultDto>.Failure(valError.Value);
        }

        var action = command.Action.Trim().ToLowerInvariant();
        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;
        var now = _clock.UtcNow;

        return await _context.Companies.ExecuteInTransactionAsync(async (ctx, ct) =>
        {
            try
            {
                var postRes = await ctx.Postings.GetByIdAsync(ownerId, command.PostingId, ct);
                if (!postRes.IsSuccess || postRes.Value is null)
                {
                    return Result<ApplicationRecordResultDto>.Failure(ErrorCode.NotFound, "Posting not found.");
                }

                var posting = postRes.Value;

                if (action == "create")
                {
                    return await HandleCreateAsync(ctx, posting, command, ownerId, now, ct);
                }
                else
                {
                    return await HandleAppendEventAsync(ctx, posting, command, ownerId, now, ct);
                }
            }
            catch (Exception ex) when (IsDuplicateKeyException(ex))
            {
                return Result<ApplicationRecordResultDto>.Failure(
                    ErrorCode.InvalidTransition,
                    "An application already exists for this posting or state transition conflict occurred.");
            }
        }, cancellationToken);
    }

    private static async Task<Result<ApplicationRecordResultDto>> HandleCreateAsync(
        ITrackingTransactionContext ctx,
        Posting posting,
        RecordApplicationCommand command,
        string ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existingAppRes = await ctx.Applications.GetByPostingIdAsync(ownerId, posting.Id, cancellationToken);
        if (existingAppRes.IsSuccess && existingAppRes.Value is not null)
        {
            return Result<ApplicationRecordResultDto>.Failure(
                ErrorCode.InvalidTransition,
                "An application already exists for this posting.");
        }

        var isOverride = false;
        if (posting.Disposition == PostingDisposition.Applied)
        {
            return Result<ApplicationRecordResultDto>.Failure(
                ErrorCode.InvalidTransition,
                "Posting is already in applied status.");
        }

        if (posting.Disposition == PostingDisposition.Rejected)
        {
            return Result<ApplicationRecordResultDto>.Failure(
                ErrorCode.InvalidTransition,
                "Cannot apply to rejected posting without explicit reopen.");
        }

        if (posting.Disposition is PostingDisposition.ScreenedOut or PostingDisposition.Closed)
        {
            if (!command.OverrideDecided)
            {
                return Result<ApplicationRecordResultDto>.Failure(
                    ErrorCode.InvalidTransition,
                    $"Cannot apply to decided posting in '{posting.Disposition.ToSnakeCase()}' status without overrideDecided flag.");
            }

            if (string.IsNullOrWhiteSpace(command.OverrideReason))
            {
                return Result<ApplicationRecordResultDto>.Failure(
                    ErrorCode.ValidationError,
                    "Reopen-and-apply override requires an explicit non-empty overrideReason.");
            }

            isOverride = true;
        }

        _ = TrackingEnumExtensions.TryParseResumeVariant(command.ResumeVariant, out var variant);
        var salary = new DesiredSalary(
            command.DesiredSalaryMin,
            command.DesiredSalaryMax,
            command.DesiredSalaryCurrency ?? "USD");

        ApplicationEntity application;
        try
        {
            application = ApplicationEntity.Create(
                ownerId: ownerId,
                postingId: posting.Id,
                submittedDate: command.SubmittedDate!,
                resumeVariant: variant,
                desiredSalary: salary,
                targetStartDate: command.TargetStartDate,
                resumePointer: command.ResumePointer,
                referralContactId: command.ReferralContactId,
                confirmationCode: command.ConfirmationCode,
                now: now);
        }
        catch (ArgumentException ex)
        {
            return Result<ApplicationRecordResultDto>.Failure(ErrorCode.ValidationError, ex.Message);
        }

        var initialEvent = ApplicationEvent.Create(
            ownerId: ownerId,
            applicationId: application.Id,
            eventDate: command.SubmittedDate!,
            eventType: ApplicationEventType.Applied,
            eventSource: "application_record",
            notes: isOverride ? $"Application submitted with override: {command.OverrideReason}" : "Application submitted",
            now: now);

        var priorDisposition = posting.Disposition.ToSnakeCase();
        var priorPolicy = posting.ResurfacePolicy.ToSnakeCase();

        posting.TransitionToApplied(now);

        await ctx.Postings.UpsertPostingAsync(posting, cancellationToken);
        await ctx.Applications.UpsertApplicationAsync(application, cancellationToken);
        await ctx.Applications.AppendEventAsync(initialEvent, cancellationToken);

        var details = AuditDetails.ForEntity(
            changedFields: ["posting_id", "submitted_date", "resume_variant", "outcome"],
            before: null,
            after: new Dictionary<string, object?>
            {
                ["posting_id"] = posting.Id,
                ["submitted_date"] = command.SubmittedDate,
                ["outcome"] = application.Outcome.ToSnakeCase(),
                ["override_applied"] = isOverride
            });

        var auditEntry = AuditLogEntry.CreateForTracking(
            ownerId: ownerId,
            actor: command.Actor,
            operation: AuditOperation.ApplicationCreate,
            entityType: "application",
            entityId: application.Id,
            resultingVersionToken: application.VersionToken,
            occurredAtUtc: now.UtcDateTime,
            details: details);

        await ctx.AppendAuditEntryAsync(auditEntry, cancellationToken);

        return Result<ApplicationRecordResultDto>.Success(new ApplicationRecordResultDto(
            ApplicationId: application.Id,
            PostingId: posting.Id,
            CurrentOutcome: "applied",
            OverrideApplied: isOverride,
            EventCount: 1,
            UpdatedAtUtc: now));
    }

    private static async Task<Result<ApplicationRecordResultDto>> HandleAppendEventAsync(
        ITrackingTransactionContext ctx,
        Posting posting,
        RecordApplicationCommand command,
        string ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var appRes = await ctx.Applications.GetByPostingIdAsync(ownerId, posting.Id, cancellationToken);
        if (!appRes.IsSuccess || appRes.Value is null)
        {
            return Result<ApplicationRecordResultDto>.Failure(
                ErrorCode.NotFound,
                "Application not found for posting.");
        }

        var app = appRes.Value;
        _ = TrackingEnumExtensions.TryParseApplicationEventType(command.Event!.EventType, out var eventType);

        ApplicationEvent appEvent;
        try
        {
            appEvent = ApplicationEvent.Create(
                ownerId: ownerId,
                applicationId: app.Id,
                eventDate: command.Event.EventDate,
                eventType: eventType,
                eventSource: command.Event.EventSource,
                notes: command.Event.Notes,
                now: now);
        }
        catch (ArgumentException ex)
        {
            return Result<ApplicationRecordResultDto>.Failure(ErrorCode.ValidationError, ex.Message);
        }

        app.ApplyEvent(eventType, command.Event.Notes, now);

        // Synchronize posting disposition unless posting is already in a terminal/decided state
        if (posting.Disposition is not PostingDisposition.Rejected and not PostingDisposition.Closed)
        {
            switch (eventType)
            {
                case ApplicationEventType.Offer:
                    // Non-terminal; posting stays in applied
                    break;

                case ApplicationEventType.Rejected:
                    posting.SynchronizeFromApplicationEvent(PostingDisposition.Rejected, command.Event.Notes, now);
                    break;

                case ApplicationEventType.OfferAccepted:
                case ApplicationEventType.OfferDeclined:
                case ApplicationEventType.Withdrawn:
                    posting.SynchronizeFromApplicationEvent(PostingDisposition.Closed, command.Event.Notes, now);
                    break;
            }
        }

        await ctx.Postings.UpsertPostingAsync(posting, cancellationToken);
        await ctx.Applications.UpsertApplicationAsync(app, cancellationToken);
        await ctx.Applications.AppendEventAsync(appEvent, cancellationToken);

        var eventsRes = await ctx.Applications.GetEventsAsync(ownerId, app.Id, cancellationToken);
        var eventCount = eventsRes.IsSuccess ? eventsRes.Value!.Count : 1;

        var details = AuditDetails.ForEntity(
            changedFields: ["outcome"],
            before: new Dictionary<string, object?> { ["outcome"] = app.Outcome.ToSnakeCase() },
            after: new Dictionary<string, object?> { ["event_type"] = eventType.ToSnakeCase(), ["event_date"] = command.Event.EventDate });

        var auditEntry = AuditLogEntry.CreateForTracking(
            ownerId: ownerId,
            actor: command.Actor,
            operation: AuditOperation.ApplicationEventAppend,
            entityType: "application",
            entityId: app.Id,
            resultingVersionToken: app.VersionToken,
            occurredAtUtc: now.UtcDateTime,
            details: details);

        await ctx.AppendAuditEntryAsync(auditEntry, cancellationToken);

        return Result<ApplicationRecordResultDto>.Success(new ApplicationRecordResultDto(
            ApplicationId: app.Id,
            PostingId: posting.Id,
            CurrentOutcome: app.Outcome.ToSnakeCase(),
            OverrideApplied: false,
            EventCount: eventCount,
            UpdatedAtUtc: now));
    }

    private static bool IsDuplicateKeyException(Exception ex)
    {
        return ex.Message.Contains("E11000", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
            || (ex.InnerException is not null && IsDuplicateKeyException(ex.InnerException));
    }
}
