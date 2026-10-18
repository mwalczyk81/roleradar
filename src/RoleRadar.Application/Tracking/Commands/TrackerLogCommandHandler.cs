using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
using RoleRadar.Domain.Common;
using RoleRadar.Domain.Tracking;

namespace RoleRadar.Application.Tracking.Commands;

public sealed class TrackerLogCommandHandler : ICommandHandler<TrackerLogCommand, TrackerLogResultDto>
{
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public TrackerLogCommandHandler(ITrackingTransactionContext context, IUtcClock clock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<TrackerLogResultDto>> HandleAsync(TrackerLogCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Action))
        {
            return Result<TrackerLogResultDto>.Failure(ErrorCode.ValidationError, "Action is required for tracker_log.");
        }

        var action = command.Action.Trim().ToLowerInvariant();
        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;
        var now = _clock.UtcNow;

        return action switch
        {
            "criterion_revision" => await HandleCriterionRevisionAsync(command, ownerId, now, cancellationToken),
            "criterion_deactivate" => await HandleCriterionDeactivateAsync(command, ownerId, now, cancellationToken),
            "search_run" => Result<TrackerLogResultDto>.Failure(ErrorCode.ValidationError, "Action 'search_run' will be implemented in US7."),
            _ => Result<TrackerLogResultDto>.Failure(
                ErrorCode.ValidationError,
                $"Action '{command.Action}' is not supported by tracker_log.")
        };
    }

    private async Task<Result<TrackerLogResultDto>> HandleCriterionRevisionAsync(
        TrackerLogCommand command,
        string ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Key))
        {
            return Result<TrackerLogResultDto>.Failure(ErrorCode.ValidationError, "Key is required for criterion_revision.");
        }

        var key = command.Key.Trim();

        if (string.IsNullOrWhiteSpace(command.Category) ||
            !TrackingEnumExtensions.TryParseCriterionCategory(command.Category, out var category))
        {
            return Result<TrackerLogResultDto>.Failure(
                ErrorCode.ValidationError,
                "Category is required for criterion_revision ('hard_skip', 'weight', 'preference').");
        }

        if (string.IsNullOrWhiteSpace(command.Value))
        {
            return Result<TrackerLogResultDto>.Failure(ErrorCode.ValidationError, "Value is required for criterion_revision.");
        }

        if (string.IsNullOrWhiteSpace(command.Provenance) ||
            !TrackingEnumExtensions.TryParseProvenanceKind(command.Provenance, out var provenance))
        {
            return Result<TrackerLogResultDto>.Failure(
                ErrorCode.ValidationError,
                "Provenance is required for criterion_revision ('stated', 'inferred').");
        }

        if (string.IsNullOrWhiteSpace(command.EffectiveFrom) ||
            !DateTimeOffset.TryParse(command.EffectiveFrom, System.Globalization.CultureInfo.InvariantCulture, out var effectiveFrom))
        {
            return Result<TrackerLogResultDto>.Failure(
                ErrorCode.ValidationError,
                "EffectiveFrom timestamp is required for criterion_revision (ISO 8601).");
        }

        var activeRes = await _context.Criteria.GetActiveByKeyAsync(ownerId, key, cancellationToken);
        if (!activeRes.IsSuccess)
        {
            return Result<TrackerLogResultDto>.Failure(activeRes.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to retrieve active criterion."));
        }

        var currentActive = activeRes.Value;

        if (currentActive is null)
        {
            // Initial version creation: insert active version
            Criterion newCriterion;
            try
            {
                newCriterion = Criterion.Create(
                    ownerId: ownerId,
                    key: key,
                    category: category,
                    value: command.Value,
                    provenance: provenance,
                    effectiveFrom: effectiveFrom,
                    createdAt: now);
            }
            catch (ArgumentException ex)
            {
                return Result<TrackerLogResultDto>.Failure(ErrorCode.ValidationError, ex.Message);
            }

            var upsertRes = await _context.Criteria.UpsertCriterionAsync(newCriterion, cancellationToken);
            if (!upsertRes.IsSuccess)
            {
                return Result<TrackerLogResultDto>.Failure(upsertRes.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to persist new criterion."));
            }

            var auditSummary = new CriterionAuditSummary(
                CriterionId: newCriterion.Id,
                Key: newCriterion.Key,
                Action: "criterion_create",
                Timestamp: now);

            await AppendAuditAsync(
                ownerId,
                "system",
                "criterion_create",
                newCriterion.Id,
                newCriterion.VersionToken,
                auditSummary,
                cancellationToken);

            return Result<TrackerLogResultDto>.Success(new TrackerLogResultDto(
                Action: "criterion_revision",
                RecordId: newCriterion.Id,
                Status: "success",
                UpdatedAtUtc: now,
                VersionToken: newCriterion.VersionToken));
        }

        // Active version exists: version token is mandatory
        if (!command.VersionToken.HasValue || command.VersionToken.Value != currentActive.VersionToken)
        {
            return Result<TrackerLogResultDto>.Failure(
                ErrorCode.StaleVersionToken,
                "Active criterion version token mismatch or missing.");
        }

        Criterion replacement;
        try
        {
            replacement = currentActive.Supersede(
                newCategory: category,
                newValue: command.Value,
                newProvenance: provenance,
                newEffectiveFrom: effectiveFrom,
                now: now);
        }
        catch (ArgumentException ex)
        {
            return Result<TrackerLogResultDto>.Failure(ErrorCode.ValidationError, ex.Message);
        }

        // Persist both closed previous version and new active replacement
        var closeOldRes = await _context.Criteria.UpsertCriterionAsync(currentActive, cancellationToken);
        if (!closeOldRes.IsSuccess)
        {
            return Result<TrackerLogResultDto>.Failure(closeOldRes.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to close previous criterion version."));
        }

        var saveNewRes = await _context.Criteria.UpsertCriterionAsync(replacement, cancellationToken);
        if (!saveNewRes.IsSuccess)
        {
            return Result<TrackerLogResultDto>.Failure(saveNewRes.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to persist superseded criterion version."));
        }

        var revisionAuditSummary = new CriterionAuditSummary(
            CriterionId: replacement.Id,
            Key: replacement.Key,
            Action: "criterion_revision",
            Timestamp: now);

        await AppendAuditAsync(
            ownerId,
            "system",
            "criterion_revision",
            replacement.Id,
            replacement.VersionToken,
            revisionAuditSummary,
            cancellationToken);

        return Result<TrackerLogResultDto>.Success(new TrackerLogResultDto(
            Action: "criterion_revision",
            RecordId: replacement.Id,
            Status: "success",
            UpdatedAtUtc: now,
            VersionToken: replacement.VersionToken));
    }

    private async Task<Result<TrackerLogResultDto>> HandleCriterionDeactivateAsync(
        TrackerLogCommand command,
        string ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Key))
        {
            return Result<TrackerLogResultDto>.Failure(ErrorCode.ValidationError, "Key is required for criterion_deactivate.");
        }

        var key = command.Key.Trim();

        var activeRes = await _context.Criteria.GetActiveByKeyAsync(ownerId, key, cancellationToken);
        if (!activeRes.IsSuccess)
        {
            return Result<TrackerLogResultDto>.Failure(activeRes.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to retrieve active criterion."));
        }

        var currentActive = activeRes.Value;
        if (currentActive is null)
        {
            return Result<TrackerLogResultDto>.Failure(
                ErrorCode.NotFound,
                $"No active criterion found for key '{key}'.");
        }

        if (!command.VersionToken.HasValue || command.VersionToken.Value != currentActive.VersionToken)
        {
            return Result<TrackerLogResultDto>.Failure(
                ErrorCode.StaleVersionToken,
                "Active criterion version token mismatch or missing.");
        }

        DateTimeOffset deactivationDate = now;
        if (!string.IsNullOrWhiteSpace(command.EffectiveFrom) &&
            DateTimeOffset.TryParse(command.EffectiveFrom, System.Globalization.CultureInfo.InvariantCulture, out var parsedDeactDate))
        {
            deactivationDate = parsedDeactDate;
        }

        currentActive.Deactivate(deactivationDate);

        var upsertRes = await _context.Criteria.UpsertCriterionAsync(currentActive, cancellationToken);
        if (!upsertRes.IsSuccess)
        {
            return Result<TrackerLogResultDto>.Failure(upsertRes.Error ?? new ResultError(ErrorCode.ValidationError, "Failed to deactivate criterion."));
        }

        var auditSummary = new CriterionAuditSummary(
            CriterionId: currentActive.Id,
            Key: currentActive.Key,
            Action: "criterion_deactivate",
            Timestamp: now);

        await AppendAuditAsync(
            ownerId,
            "system",
            "criterion_deactivate",
            currentActive.Id,
            currentActive.VersionToken,
            auditSummary,
            cancellationToken);

        return Result<TrackerLogResultDto>.Success(new TrackerLogResultDto(
            Action: "criterion_deactivate",
            RecordId: currentActive.Id,
            Status: "success",
            UpdatedAtUtc: now,
            VersionToken: currentActive.VersionToken));
    }

    private Task AppendAuditAsync(
        string ownerId,
        string actor,
        string operation,
        Guid criterionId,
        Guid versionToken,
        CriterionAuditSummary summary,
        CancellationToken cancellationToken)
    {
        var isCreate = operation == "criterion_create";
        var beforeSummary = isCreate
            ? null
            : new AuditContentSummary(
                bodyHash: "0000000000000000000000000000000000000000000000000000000000000000",
                byteLength: 0,
                status: "prior",
                excerpt: string.Empty);

        var entry = AuditLogEntry.Create(
            ownerId: ownerId,
            actor: actor,
            operation: isCreate ? AuditOperation.NoteCreate : AuditOperation.NoteAppend,
            targetNoteId: criterionId,
            noteRevisionId: null,
            appendedText: isCreate ? null : summary.Key,
            resultingVersionToken: versionToken,
            resolvedDefaultDate: null,
            occurredAtUtc: summary.Timestamp.UtcDateTime,
            beforeSummary: beforeSummary,
            afterSummary: new AuditContentSummary(
                bodyHash: "0000000000000000000000000000000000000000000000000000000000000000",
                byteLength: summary.Key.Length,
                status: operation,
                excerpt: $"{summary.Key}:{summary.Action}"));

        return _context.AppendAuditEntryAsync(entry, cancellationToken);
    }
}
