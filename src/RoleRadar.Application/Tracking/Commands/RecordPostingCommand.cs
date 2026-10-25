using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    DateTimeOffset? LastVerifiedAt = null,
    bool? CompHighPriority = null);

[JsonConverter(typeof(SignalInputDtoConverter))]
public sealed record SignalInputDto(
    string SignalType,
    string Evidence = "reported by agent",
    string? ObservedAt = null,
    string? DeadlineDate = null);

public sealed class SignalInputDtoConverter : JsonConverter<SignalInputDto>
{
    private const string DefaultEvidence = "reported by agent";

    public override SignalInputDto? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString()?.Trim() ?? string.Empty;
            if (text.StartsWith("deadline:", StringComparison.OrdinalIgnoreCase))
            {
                var deadlineDate = text["deadline:".Length..].Trim();
                return new SignalInputDto(
                    SignalType: "deadline",
                    Evidence: DefaultEvidence,
                    ObservedAt: null,
                    DeadlineDate: deadlineDate);
            }

            return new SignalInputDto(
                SignalType: text,
                Evidence: DefaultEvidence,
                ObservedAt: null,
                DeadlineDate: null);
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            string? signalType = null;
            if (root.TryGetProperty("signalType", out var stElem) || root.TryGetProperty("signal_type", out stElem))
            {
                signalType = stElem.GetString();
            }

            string? evidence = null;
            if (root.TryGetProperty("evidence", out var evElem))
            {
                evidence = evElem.GetString();
            }
            if (string.IsNullOrWhiteSpace(evidence))
            {
                evidence = DefaultEvidence;
            }

            string? deadlineDate = null;
            if (root.TryGetProperty("deadlineDate", out var dlElem) || root.TryGetProperty("deadline_date", out dlElem))
            {
                deadlineDate = dlElem.GetString();
            }

            string? observedAt = null;
            if (root.TryGetProperty("observedAt", out var obsElem) || root.TryGetProperty("observed_at", out obsElem))
            {
                observedAt = obsElem.GetString();
            }

            return new SignalInputDto(
                SignalType: signalType ?? string.Empty,
                Evidence: evidence,
                ObservedAt: observedAt,
                DeadlineDate: deadlineDate);
        }

        throw new JsonException($"Expected string or object for signal, but encountered {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, SignalInputDto value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteString("signalType", value.SignalType);
        writer.WriteString("evidence", value.Evidence);
        if (value.ObservedAt is not null)
        {
            writer.WriteString("observedAt", value.ObservedAt);
        }
        if (value.DeadlineDate is not null)
        {
            writer.WriteString("deadlineDate", value.DeadlineDate);
        }
        writer.WriteEndObject();
    }
}

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
    string? OwnerId = null,
    bool? CompHighPriority = null) : ICommand<PostingRecordResultDto>;

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

            if (command.Signals is not null)
            {
                foreach (var sig in command.Signals)
                {
                    if (sig is null)
                    {
                        return new ResultError(ErrorCode.ValidationError, "Signal item cannot be null.");
                    }

                    if (!TrackingEnumExtensions.TryParseSignalType(sig.SignalType, out _))
                    {
                        return new ResultError(ErrorCode.ValidationError, $"Invalid signalType '{sig.SignalType}'.");
                    }

                    if (!string.IsNullOrWhiteSpace(sig.DeadlineDate))
                    {
                        if (!DateOnly.TryParseExact(sig.DeadlineDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                        {
                            return new ResultError(ErrorCode.ValidationError, "Deadline date must be in YYYY-MM-DD format.");
                        }
                    }

                    if (string.IsNullOrWhiteSpace(sig.Evidence) || sig.Evidence.Trim().Length > 500)
                    {
                        return new ResultError(ErrorCode.ValidationError, "Signal evidence is required and cannot exceed 500 characters.");
                    }
                }
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

        if (command.Signals is not null)
        {
            foreach (var sig in command.Signals)
            {
                if (sig is null)
                {
                    return new ResultError(ErrorCode.ValidationError, "Signal item cannot be null.");
                }

                if (!TrackingEnumExtensions.TryParseSignalType(sig.SignalType, out _))
                {
                    return new ResultError(ErrorCode.ValidationError, $"Invalid signalType '{sig.SignalType}'.");
                }

                if (!string.IsNullOrWhiteSpace(sig.DeadlineDate))
                {
                    if (!DateOnly.TryParseExact(sig.DeadlineDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                    {
                        return new ResultError(ErrorCode.ValidationError, "Deadline date must be in YYYY-MM-DD format.");
                    }
                }

                if (string.IsNullOrWhiteSpace(sig.Evidence) || sig.Evidence.Trim().Length > 500)
                {
                    return new ResultError(ErrorCode.ValidationError, "Signal evidence is required and cannot exceed 500 characters.");
                }
            }
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
            var existingComp = posting.Compensation;
            comp = new Compensation(
                command.CompensationMin ?? existingComp?.Min,
                command.CompensationMax ?? existingComp?.Max,
                command.CompensationCurrency ?? existingComp?.Currency ?? "USD",
                command.CompensationText ?? existingComp?.Text);
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
            now,
            command.CompHighPriority);

        await _context.Postings.UpsertPostingAsync(posting, cancellationToken);

        if (command.Signals is not null && command.Signals.Count > 0)
        {
            foreach (var sig in command.Signals)
            {
                if (TrackingEnumExtensions.TryParseSignalType(sig.SignalType, out var st))
                {
                    var observedAt = !string.IsNullOrWhiteSpace(sig.ObservedAt) && DateTimeOffset.TryParse(sig.ObservedAt, out var obs)
                        ? obs
                        : now;
                    var signalEntity = new PostingSignal(
                        id: Guid.CreateVersion7(),
                        ownerId: ownerId,
                        postingId: posting.Id,
                        signalType: st,
                        evidence: sig.Evidence,
                        observedAt: observedAt,
                        deadlineDate: sig.DeadlineDate);
                    await _context.Postings.AppendSignalAsync(signalEntity, cancellationToken);
                    changedFields.Add("signals");
                }
            }
        }
        if (changedFields.Count > 0)
        {
            var summary = new PostingAuditSummary(posting.Id, changedFields, Timestamp: now);
            await AppendAuditAsync(ownerId, command.Actor, AuditOperation.PostingUpdate, posting.Id, posting.VersionToken, summary, cancellationToken);
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
        if (trimmedReqId is not null && !trimmedReqId.Any(char.IsDigit))
        {
            trimmedReqId = null;
        }
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
                // Concurrency resolution & Tier D deduplication:
                // If any candidate matches a definitive key (Tier A/B/C) or matches by company + title with compatible requisition, treat as definitive match
                foreach (var candidate in candidatesResult.Value)
                {
                    var candPostingRes = await _context.Postings.GetByIdAsync(ownerId, candidate.Id, cancellationToken);
                    if (candPostingRes.IsSuccess && candPostingRes.Value is not null)
                    {
                        var cand = candPostingRes.Value;
                        var isDefinitiveKeyMatch = (!string.IsNullOrWhiteSpace(dedupKeyA) && cand.DedupKeyA == dedupKeyA)
                                                || (!string.IsNullOrWhiteSpace(dedupKeyB) && cand.DedupKeyB == dedupKeyB)
                                                || (!string.IsNullOrWhiteSpace(dedupKeyC) && cand.DedupKeyC == dedupKeyC);

                        // Tier D match: identical company + title if candidate has no req, or incoming has no req, or both share the same req
                        var isTierDMatch = string.IsNullOrWhiteSpace(cand.RequisitionId)
                                        || string.IsNullOrWhiteSpace(trimmedReqId)
                                        || string.Equals(cand.RequisitionId, trimmedReqId, StringComparison.OrdinalIgnoreCase);

                        if (isDefinitiveKeyMatch || isTierDMatch)
                        {
                            return await HandleDedupMatchAsync(cand, command, ownerId, now, cancellationToken);
                        }
                    }
                }

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
            now: now,
            compHighPriority: command.CompHighPriority);

        await _context.Postings.UpsertPostingAsync(newPosting, cancellationToken);

        // Append initial signals if present
        if (command.Signals is not null && command.Signals.Count > 0)
        {
            foreach (var sig in command.Signals)
            {
                if (TrackingEnumExtensions.TryParseSignalType(sig.SignalType, out var st))
                {
                    var observedAt = !string.IsNullOrWhiteSpace(sig.ObservedAt) && DateTimeOffset.TryParse(sig.ObservedAt, out var obs)
                        ? obs
                        : now;
                    var signalEntity = new PostingSignal(
                        id: Guid.CreateVersion7(),
                        ownerId: ownerId,
                        postingId: newPosting.Id,
                        signalType: st,
                        evidence: sig.Evidence,
                        observedAt: observedAt,
                        deadlineDate: sig.DeadlineDate);
                    await _context.Postings.AppendSignalAsync(signalEntity, cancellationToken);
                }
            }
        }

        var auditSummary = new PostingAuditSummary(newPosting.Id, ["title", "company_id", "disposition"], Timestamp: now);
        await AppendAuditAsync(ownerId, command.Actor, AuditOperation.PostingCreate, newPosting.Id, newPosting.VersionToken, auditSummary, cancellationToken);

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

        if (command.CompHighPriority.HasValue && command.CompHighPriority.Value != stored.CompHighPriority)
        {
            differences.Add(new PostingDifference("comp_high_priority", stored.CompHighPriority, command.CompHighPriority.Value));
        }

        // 3. Append any newly observed signals not already recorded today with identical type and evidence
        var appendedSignals = false;
        if (command.Signals is not null && command.Signals.Count > 0)
        {
            var existingSignalsResult = await _context.Postings.GetSignalsAsync(ownerId, stored.Id, cancellationToken);
            var existingSignals = (existingSignalsResult.IsSuccess && existingSignalsResult.Value is not null)
                ? existingSignalsResult.Value
                : Array.Empty<PostingSignal>();

            foreach (var sig in command.Signals)
            {
                if (TrackingEnumExtensions.TryParseSignalType(sig.SignalType, out var st))
                {
                    var observedAt = !string.IsNullOrWhiteSpace(sig.ObservedAt) && DateTimeOffset.TryParse(sig.ObservedAt, out var obs)
                        ? obs
                        : now;
                    var sigUtcDate = DateOnly.FromDateTime(observedAt.UtcDateTime);

                    // Duplicate check: same signalType, same evidence, same UTC day
                    var alreadyPresent = existingSignals.Any(e =>
                        e.SignalType == st &&
                        string.Equals(e.Evidence?.Trim(), sig.Evidence?.Trim(), StringComparison.Ordinal) &&
                        DateOnly.FromDateTime(e.ObservedAt.UtcDateTime) == sigUtcDate);

                    if (!alreadyPresent)
                    {
                        var signalEntity = new PostingSignal(
                            id: Guid.CreateVersion7(),
                            ownerId: ownerId,
                            postingId: stored.Id,
                            signalType: st,
                            evidence: sig.Evidence.Trim(),
                            observedAt: observedAt,
                            deadlineDate: sig.DeadlineDate);
                        await _context.Postings.AppendSignalAsync(signalEntity, cancellationToken);
                        appendedSignals = true;
                    }
                }
            }

            if (appendedSignals)
            {
                var auditSummary = new PostingAuditSummary(stored.Id, ["signals"], Timestamp: now);
                await AppendAuditAsync(ownerId, command.Actor, AuditOperation.PostingSignalAppend, stored.Id, stored.VersionToken, auditSummary, cancellationToken);
            }
        }

        // 4. UTC date comparison for last_verified_at bump
        var storedUtcDate = DateOnly.FromDateTime(stored.LastVerifiedAt.UtcDateTime);
        var currentUtcDate = DateOnly.FromDateTime(now.UtcDateTime);

        if (storedUtcDate != currentUtcDate)
        {
            stored.UpdateLastVerified(now);
            await _context.Postings.UpsertPostingAsync(stored, cancellationToken);

            var auditSummary = new PostingAuditSummary(stored.Id, ["last_verified_at"], Timestamp: now);
            await AppendAuditAsync(ownerId, command.Actor, AuditOperation.PostingVerifiedBump, stored.Id, stored.VersionToken, auditSummary, cancellationToken);
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
        var isCreate = operation == AuditOperation.PostingCreate;
        var details = AuditDetails.ForEntity(
            changedFields: summary.ChangedFieldNames,
            before: isCreate ? null : new Dictionary<string, object?>(),
            after: new Dictionary<string, object?>
            {
                ["changed_field_count"] = summary.ChangedFieldNames.Count
            });

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
            LastVerifiedAt: p.LastVerifiedAt,
            CompHighPriority: p.CompHighPriority);
}
