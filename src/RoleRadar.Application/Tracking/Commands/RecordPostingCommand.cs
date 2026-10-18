using RoleRadar.Application.Abstractions;
using RoleRadar.Domain.Audit;
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

public sealed class RecordPostingCommandValidator : ICommandValidator<RecordPostingCommand>
{
    public ResultError? Validate(RecordPostingCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Explicit update mode
        if (command.PostingId.HasValue)
        {
            if (!command.VersionToken.HasValue)
            {
                return new ResultError(ErrorCode.StaleVersionToken, "VersionToken is required when updating a posting.");
            }

            if (command.Title is not null && (string.IsNullOrWhiteSpace(command.Title) || command.Title.Trim().Length > Posting.MaxTitleLength))
            {
                return new ResultError(ErrorCode.ValidationError, $"Title must be 1..{Posting.MaxTitleLength} characters.");
            }

            if (command.Url is not null && command.Url.Length > Posting.MaxUrlLength)
            {
                return new ResultError(ErrorCode.ValidationError, $"Url cannot exceed {Posting.MaxUrlLength} characters.");
            }

            if (command.Score.HasValue && (command.Score.Value < 0.0m || command.Score.Value > 10.0m))
            {
                return new ResultError(ErrorCode.ValidationError, "Score must be between 0.0 and 10.0.");
            }

            return null;
        }

        // Create / Ingest mode
        if (!command.CompanyId.HasValue || command.CompanyId.Value == Guid.Empty)
        {
            return new ResultError(ErrorCode.ValidationError, "CompanyId is required for new posting.");
        }

        if (string.IsNullOrWhiteSpace(command.Title))
        {
            return new ResultError(ErrorCode.ValidationError, "Title is required and cannot be blank.");
        }

        if (command.Title.Trim().Length > Posting.MaxTitleLength)
        {
            return new ResultError(ErrorCode.ValidationError, $"Title cannot exceed {Posting.MaxTitleLength} characters.");
        }

        if (command.Url is not null && command.Url.Length > Posting.MaxUrlLength)
        {
            return new ResultError(ErrorCode.ValidationError, $"Url cannot exceed {Posting.MaxUrlLength} characters.");
        }

        if (command.Score.HasValue && (command.Score.Value < 0.0m || command.Score.Value > 10.0m))
        {
            return new ResultError(ErrorCode.ValidationError, "Score must be between 0.0 and 10.0.");
        }

        return null;
    }
}

public sealed class RecordPostingCommandHandler : ICommandHandler<RecordPostingCommand, PostingRecordResultDto>
{
    private static readonly RecordPostingCommandValidator Validator = new();
    private readonly ITrackingTransactionContext _context;
    private readonly IUtcClock _clock;

    public RecordPostingCommandHandler(ITrackingTransactionContext context, IUtcClock clock)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<Result<PostingRecordResultDto>> HandleAsync(
        RecordPostingCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var valError = Validator.Validate(command);
        if (valError is not null)
        {
            return Result<PostingRecordResultDto>.Failure(valError.Value);
        }

        var ownerId = string.IsNullOrWhiteSpace(command.OwnerId)
            ? OwnerId.Default.Value
            : new OwnerId(command.OwnerId).Value;
        var now = _clock.UtcNow;

        // 1. Explicit Update Mode
        if (command.PostingId.HasValue)
        {
            return await HandleExplicitUpdateAsync(command, ownerId, now, cancellationToken);
        }

        // 2. Ingest / Create Mode
        return await HandleIngestOrCreateAsync(command, ownerId, now, cancellationToken);
    }

    private async Task<Result<PostingRecordResultDto>> HandleExplicitUpdateAsync(
        RecordPostingCommand command,
        string ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var postingResult = await _context.Postings.GetByIdAsync(ownerId, command.PostingId!.Value, cancellationToken);
        if (!postingResult.IsSuccess || postingResult.Value is null)
        {
            return Result<PostingRecordResultDto>.Failure(ErrorCode.NotFound, "Posting not found.");
        }

        var posting = postingResult.Value;
        if (posting.VersionToken != command.VersionToken!.Value)
        {
            return Result<PostingRecordResultDto>.Failure(ErrorCode.StaleVersionToken, "Posting version token mismatch or stale.");
        }

        if (command.CompanyId.HasValue && command.CompanyId.Value != posting.CompanyId)
        {
            var companyCheck = await _context.Companies.GetByIdAsync(ownerId, command.CompanyId.Value, cancellationToken);
            if (!companyCheck.IsSuccess || companyCheck.Value is null)
            {
                return Result<PostingRecordResultDto>.Failure(ErrorCode.ValidationError, $"Target company '{command.CompanyId.Value}' does not exist.");
            }
        }

        SourceKind? sk = null;
        if (TrackingEnumExtensions.TryParseSourceKind(command.SourceKind, out var parsedSk))
        {
            sk = parsedSk;
        }

        RemoteType? rt = null;
        if (TrackingEnumExtensions.TryParseRemoteType(command.RemoteType, out var parsedRt))
        {
            rt = parsedRt;
        }

        CultureLevel? cl = null;
        if (TrackingEnumExtensions.TryParseCultureLevel(command.CultureLevel, out var parsedCl))
        {
            cl = parsedCl;
        }

        Compensation? comp = null;
        if (command.CompensationMin.HasValue || command.CompensationMax.HasValue || command.CompensationCurrency is not null || command.CompensationText is not null)
        {
            comp = new Compensation(
                command.CompensationMin,
                command.CompensationMax,
                command.CompensationCurrency ?? "USD",
                command.CompensationText);
        }

        // Duplicate collapsing if duplicateOfId passed
        Guid? collapsedDuplicateOfId = command.DuplicateOfId;
        if (collapsedDuplicateOfId.HasValue)
        {
            if (collapsedDuplicateOfId.Value == posting.Id)
            {
                return Result<PostingRecordResultDto>.Failure(ErrorCode.ValidationError, "A posting cannot be a duplicate of itself.");
            }
            var rootResult = await ResolveRootDuplicateAsync(ownerId, collapsedDuplicateOfId.Value, cancellationToken);
            if (!rootResult.IsSuccess)
            {
                return Result<PostingRecordResultDto>.Failure(rootResult.Error!.Value);
            }
            collapsedDuplicateOfId = rootResult.Value;
        }

        var changedFields = posting.ApplyExplicitUpdate(
            command.CompanyId,
            command.Title,
            command.Url,
            sk,
            command.AtsTenant,
            command.RequisitionId,
            command.Location,
            rt,
            comp,
            command.PostedDate,
            command.Score,
            cl,
            command.CultureSummary,
            collapsedDuplicateOfId,
            command.ScreeningNoteId,
            now);

        await _context.Postings.UpsertPostingAsync(posting, cancellationToken);

        if (changedFields.Count > 0)
        {
            var summary = new PostingAuditSummary(posting.Id, changedFields, Timestamp: now);
            await AppendAuditAsync(ownerId, command.Actor, "posting_update", posting.Id, posting.VersionToken, summary, cancellationToken);
        }

        return Result<PostingRecordResultDto>.Success(new PostingRecordResultDto(
            Status: "updated",
            Posting: MapToDto(posting)));
    }

    private async Task<Result<PostingRecordResultDto>> HandleIngestOrCreateAsync(
        RecordPostingCommand command,
        string ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // 1. Verify company exists
        var companyCheck = await _context.Companies.GetByIdAsync(ownerId, command.CompanyId!.Value, cancellationToken);
        if (!companyCheck.IsSuccess || companyCheck.Value is null)
        {
            return Result<PostingRecordResultDto>.Failure(ErrorCode.ValidationError, $"Target company '{command.CompanyId.Value}' does not exist in companies store.");
        }

        var trimmedTenant = string.IsNullOrWhiteSpace(command.AtsTenant) ? null : command.AtsTenant.Trim();
        var trimmedReqId = string.IsNullOrWhiteSpace(command.RequisitionId) ? null : command.RequisitionId.Trim();
        var normalizedUrl = UrlNormalizer.Normalize(command.Url);

        string? dedupKeyA = (!string.IsNullOrWhiteSpace(trimmedTenant) && !string.IsNullOrWhiteSpace(trimmedReqId))
            ? $"{trimmedTenant}|{trimmedReqId}"
            : null;
        string? dedupKeyB = normalizedUrl;
        string? dedupKeyC = (string.IsNullOrWhiteSpace(trimmedTenant) && !string.IsNullOrWhiteSpace(trimmedReqId))
            ? $"{command.CompanyId.Value:D}|{trimmedReqId}"
            : null;

        Posting? matchedPosting = null;

        // Tier A
        if (!string.IsNullOrWhiteSpace(dedupKeyA))
        {
            var matchA = await _context.Postings.GetByDedupKeyAAsync(ownerId, dedupKeyA, cancellationToken);
            if (matchA.IsSuccess && matchA.Value is not null)
            {
                matchedPosting = matchA.Value;
            }
        }

        // Tier B
        if (matchedPosting is null && !string.IsNullOrWhiteSpace(dedupKeyB))
        {
            var matchB = await _context.Postings.GetByDedupKeyBAsync(ownerId, dedupKeyB, cancellationToken);
            if (matchB.IsSuccess && matchB.Value is not null)
            {
                matchedPosting = matchB.Value;
                // Upgrade in-place if ATS identifiers newly supplied
                if (!string.IsNullOrWhiteSpace(trimmedTenant) || !string.IsNullOrWhiteSpace(trimmedReqId))
                {
                    matchedPosting.UpgradeIdentifiers(trimmedTenant, trimmedReqId, now);
                    await _context.Postings.UpsertPostingAsync(matchedPosting, cancellationToken);
                }
            }
        }

        // Tier C
        if (matchedPosting is null && !string.IsNullOrWhiteSpace(dedupKeyC))
        {
            var matchC = await _context.Postings.GetByDedupKeyCAsync(ownerId, dedupKeyC, cancellationToken);
            if (matchC.IsSuccess && matchC.Value is not null)
            {
                matchedPosting = matchC.Value;
            }
        }

        // Tier D Advisory check
        var normalizedTitle = TitleNormalizer.Normalize(command.Title!);
        if (matchedPosting is null)
        {
            var candidatesResult = await _context.Postings.FindCandidatesByCompanyAndTitleAsync(
                ownerId, command.CompanyId.Value, normalizedTitle, cancellationToken);
            if (candidatesResult.IsSuccess && candidatesResult.Value is not null && candidatesResult.Value.Count > 0)
            {
                return Result<PostingRecordResultDto>.Success(new PostingRecordResultDto(
                    Status: "possible_duplicate",
                    Candidates: candidatesResult.Value));
            }
        }

        // Handle Dedup Match (Tiers A-C)
        if (matchedPosting is not null)
        {
            return await HandleDedupMatchAsync(matchedPosting, command, ownerId, now, cancellationToken);
        }

        // Collapse duplicateOfId if supplied
        Guid? collapsedDuplicateOfId = command.DuplicateOfId;
        if (collapsedDuplicateOfId.HasValue)
        {
            var rootResult = await ResolveRootDuplicateAsync(ownerId, collapsedDuplicateOfId.Value, cancellationToken);
            if (!rootResult.IsSuccess)
            {
                return Result<PostingRecordResultDto>.Failure(rootResult.Error!.Value);
            }
            collapsedDuplicateOfId = rootResult.Value;
        }

        // Create New Posting
        _ = TrackingEnumExtensions.TryParseSourceKind(command.SourceKind, out var sourceKind);
        _ = TrackingEnumExtensions.TryParseRemoteType(command.RemoteType, out var remoteType);
        _ = TrackingEnumExtensions.TryParseCultureLevel(command.CultureLevel, out var cultureLevel);
        _ = TrackingEnumExtensions.TryParseResurfacePolicy(command.ResurfacePolicy, out var resurfacePolicy);

        Compensation? comp = null;
        if (command.CompensationMin.HasValue || command.CompensationMax.HasValue || command.CompensationCurrency is not null || command.CompensationText is not null)
        {
            comp = new Compensation(
                command.CompensationMin,
                command.CompensationMax,
                command.CompensationCurrency ?? "USD",
                command.CompensationText);
        }

        var newPosting = Posting.Create(
            ownerId: ownerId,
            companyId: command.CompanyId.Value,
            title: command.Title!,
            sourceKind: sourceKind,
            originalUrl: command.Url,
            atsTenant: trimmedTenant,
            requisitionId: trimmedReqId,
            location: command.Location,
            remoteType: remoteType,
            compensation: comp,
            postedDate: command.PostedDate,
            score: command.Score,
            cultureLevel: cultureLevel,
            cultureSummary: command.CultureSummary,
            disposition: PostingDisposition.Lead,
            resurfacePolicy: resurfacePolicy,
            duplicateOfId: collapsedDuplicateOfId,
            screeningNoteId: command.ScreeningNoteId,
            now: now);

        await _context.Postings.UpsertPostingAsync(newPosting, cancellationToken);

        // Append initial signals if present
        if (command.Signals is not null && command.Signals.Count > 0)
        {
            foreach (var sig in command.Signals)
            {
                if (Enum.TryParse<SignalType>(sig.SignalType, true, out var st))
                {
                    var observedAt = !string.IsNullOrWhiteSpace(sig.ObservedAt) && DateTimeOffset.TryParse(sig.ObservedAt, out var obs)
                        ? obs
                        : now;
                    var signalEntity = new PostingSignal(
                        Id: Guid.CreateVersion7(),
                        OwnerId: ownerId,
                        PostingId: newPosting.Id,
                        SignalType: st,
                        Evidence: sig.Evidence,
                        ObservedAt: observedAt,
                        DeadlineDate: sig.DeadlineDate);
                    await _context.Postings.AppendSignalAsync(signalEntity, cancellationToken);
                }
            }
        }

        var auditSummary = new PostingAuditSummary(newPosting.Id, ["title", "company_id", "disposition"], Timestamp: now);
        await AppendAuditAsync(ownerId, command.Actor, "posting_create", newPosting.Id, newPosting.VersionToken, auditSummary, cancellationToken);

        return Result<PostingRecordResultDto>.Success(new PostingRecordResultDto(
            Status: "created",
            Posting: MapToDto(newPosting)));
    }

    private async Task<Result<PostingRecordResultDto>> HandleDedupMatchAsync(
        Posting stored,
        RecordPostingCommand command,
        string ownerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // 1. If Decided (screened_out, closed, rejected)
        if (stored.Disposition == PostingDisposition.ScreenedOut ||
            stored.Disposition == PostingDisposition.Closed ||
            stored.Disposition == PostingDisposition.Rejected)
        {
            var canReopen = stored.ResurfacePolicy == ResurfacePolicy.IfReopened &&
                            stored.Disposition != PostingDisposition.Rejected;

            return Result<PostingRecordResultDto>.Success(new PostingRecordResultDto(
                Status: "already_decided",
                Posting: MapToDto(stored),
                PriorReason: stored.DispositionReason,
                CanReopen: canReopen));
        }

        // 2. Active posting: Check for differing supplied fields (Differences list)
        var differences = new List<PostingDifference>();
        if (!string.IsNullOrWhiteSpace(command.Location) && command.Location.Trim() != stored.Location)
        {
            differences.Add(new PostingDifference("location", stored.Location, command.Location.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(command.RemoteType) &&
            Enum.TryParse<RemoteType>(command.RemoteType, true, out var reqRt) &&
            reqRt != stored.RemoteType)
        {
            differences.Add(new PostingDifference("remote_type", stored.RemoteType.ToString().ToLowerInvariant(), reqRt.ToString().ToLowerInvariant()));
        }

        if (command.CompensationMin.HasValue && command.CompensationMin != stored.Compensation.Min)
        {
            differences.Add(new PostingDifference("compensation_min", stored.Compensation.Min, command.CompensationMin.Value));
        }

        if (command.CompensationMax.HasValue && command.CompensationMax != stored.Compensation.Max)
        {
            differences.Add(new PostingDifference("compensation_max", stored.Compensation.Max, command.CompensationMax.Value));
        }

        // 3. UTC date comparison for last_verified_at bump
        var storedUtcDate = DateOnly.FromDateTime(stored.LastVerifiedAt.UtcDateTime);
        var currentUtcDate = DateOnly.FromDateTime(now.UtcDateTime);

        if (storedUtcDate != currentUtcDate)
        {
            stored.UpdateLastVerified(now);
            await _context.Postings.UpsertPostingAsync(stored, cancellationToken);

            var auditSummary = new PostingAuditSummary(stored.Id, ["last_verified_at"], Timestamp: now);
            await AppendAuditAsync(ownerId, command.Actor, "posting_verify", stored.Id, stored.VersionToken, auditSummary, cancellationToken);
        }

        return Result<PostingRecordResultDto>.Success(new PostingRecordResultDto(
            Status: "already_tracked",
            Posting: MapToDto(stored),
            Differences: differences.Count > 0 ? differences : null));
    }

    private async Task<Result<Guid>> ResolveRootDuplicateAsync(string ownerId, Guid duplicateOfId, CancellationToken cancellationToken)
    {
        var visited = new HashSet<Guid>();
        var currentId = duplicateOfId;

        while (true)
        {
            if (!visited.Add(currentId))
            {
                return Result<Guid>.Failure(ErrorCode.ValidationError, "Circular duplicate reference chain detected.");
            }

            var next = await _context.Postings.GetByIdAsync(ownerId, currentId, cancellationToken);
            if (!next.IsSuccess || next.Value is null)
            {
                // Target duplicate doesn't exist or is root
                return Result<Guid>.Success(currentId);
            }

            if (!next.Value.DuplicateOfId.HasValue)
            {
                return Result<Guid>.Success(currentId);
            }

            currentId = next.Value.DuplicateOfId.Value;
        }
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
        var isCreate = operation == "posting_create";
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
            targetNoteId: postingId,
            noteRevisionId: null,
            appendedText: isCreate ? null : string.Join(",", summary.ChangedFieldNames),
            resultingVersionToken: versionToken,
            resolvedDefaultDate: null,
            occurredAtUtc: summary.Timestamp.UtcDateTime,
            beforeSummary: beforeSummary,
            afterSummary: new AuditContentSummary(
                bodyHash: "0000000000000000000000000000000000000000000000000000000000000000",
                byteLength: summary.ChangedFieldNames.Count,
                status: operation,
                excerpt: string.Join(",", summary.ChangedFieldNames)));

        return _context.AppendAuditEntryAsync(entry, cancellationToken);
    }

    private static PostingDto MapToDto(Posting p) =>
        new(
            Id: p.Id,
            CompanyId: p.CompanyId,
            Title: p.Title,
            NormalizedTitle: p.NormalizedTitle,
            Disposition: p.Disposition.ToSnakeCase(),
            ResurfacePolicy: p.ResurfacePolicy.ToSnakeCase(),
            VersionToken: p.VersionToken,
            Location: p.Location,
            RemoteType: p.RemoteType.ToSnakeCase(),
            Score: p.Score,
            CultureLevel: p.CultureLevel.ToSnakeCase(),
            LastVerifiedAt: p.LastVerifiedAt);
}
