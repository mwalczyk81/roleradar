using RoleRadar.Domain.Audit;

namespace RoleRadar.Domain.Tracking;

public sealed record PostingDifference(
    string Field,
    object? StoredValue,
    object? SuppliedValue);

public sealed record PostingCandidate(
    Guid Id,
    string Title,
    PostingDisposition Disposition,
    string? Reason);


public sealed class Posting
{
    public const int MaxTitleLength = 200;
    public const int MaxUrlLength = 1000;
    public const int MaxAtsTenantLength = 100;
    public const int MaxRequisitionIdLength = 100;
    public const int MaxLocationLength = 150;
    public const int MaxCompensationTextLength = 300;
    public const int MaxCultureSummaryLength = 500;
    public const int MaxDispositionReasonLength = 500;

    public Guid Id { get; init; }
    public string OwnerId { get; init; } = string.Empty;
    public Guid CompanyId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string NormalizedTitle { get; private set; } = string.Empty;
    public string? OriginalUrl { get; private set; }
    public string? NormalizedUrl { get; private set; }
    public SourceKind SourceKind { get; private set; } = SourceKind.Ats;
    public string? AtsTenant { get; private set; }
    public string? RequisitionId { get; private set; }
    public string? DedupKeyA { get; private set; }
    public string? DedupKeyB { get; private set; }
    public string? DedupKeyC { get; private set; }
    public string? Location { get; private set; }
    public RemoteType RemoteType { get; private set; } = RemoteType.Unknown;
    public Compensation Compensation { get; private set; } = new();
    public string? PostedDate { get; private set; }
    public DateTimeOffset FirstSeenAt { get; init; }
    public DateTimeOffset LastVerifiedAt { get; private set; }
    public string? ClosedDate { get; private set; }
    public decimal? Score { get; private set; }
    public CultureLevel CultureLevel { get; private set; } = CultureLevel.Unknown;
    public string? CultureSummary { get; private set; }
    public PostingDisposition Disposition { get; private set; } = PostingDisposition.Lead;
    public string? DispositionReason { get; private set; }
    public Guid? DispositionCriterionId { get; private set; }
    public ResurfacePolicy ResurfacePolicy { get; private set; } = ResurfacePolicy.Never;
    public DateTimeOffset? DecidedAt { get; private set; }
    public Guid? DuplicateOfId { get; private set; }
    public Guid? ScreeningNoteId { get; private set; }
    public Guid VersionToken { get; private set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public static Posting Create(
        string ownerId,
        Guid companyId,
        string title,
        SourceKind sourceKind = SourceKind.Ats,
        string? originalUrl = null,
        string? atsTenant = null,
        string? requisitionId = null,
        string? location = null,
        RemoteType remoteType = RemoteType.Unknown,
        Compensation? compensation = null,
        string? postedDate = null,
        decimal? score = null,
        CultureLevel cultureLevel = CultureLevel.Unknown,
        string? cultureSummary = null,
        PostingDisposition disposition = PostingDisposition.Lead,
        string? dispositionReason = null,
        Guid? dispositionCriterionId = null,
        ResurfacePolicy resurfacePolicy = ResurfacePolicy.Never,
        DateTimeOffset? decidedAt = null,
        Guid? duplicateOfId = null,
        Guid? screeningNoteId = null,
        Guid? id = null,
        DateTimeOffset? now = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length > MaxTitleLength)
        {
            throw new ArgumentException($"Posting title cannot exceed {MaxTitleLength} characters.", nameof(title));
        }

        if (originalUrl is not null && originalUrl.Length > MaxUrlLength)
        {
            throw new ArgumentException($"Original URL cannot exceed {MaxUrlLength} characters.", nameof(originalUrl));
        }

        if (atsTenant is not null && atsTenant.Length > MaxAtsTenantLength)
        {
            throw new ArgumentException($"ATS tenant cannot exceed {MaxAtsTenantLength} characters.", nameof(atsTenant));
        }

        if (requisitionId is not null && requisitionId.Length > MaxRequisitionIdLength)
        {
            throw new ArgumentException($"Requisition ID cannot exceed {MaxRequisitionIdLength} characters.", nameof(requisitionId));
        }

        if (location is not null && location.Length > MaxLocationLength)
        {
            throw new ArgumentException($"Location cannot exceed {MaxLocationLength} characters.", nameof(location));
        }

        if (compensation?.Text is not null && compensation.Text.Length > MaxCompensationTextLength)
        {
            throw new ArgumentException($"Compensation text cannot exceed {MaxCompensationTextLength} characters.", nameof(compensation));
        }

        if (cultureSummary is not null && cultureSummary.Length > MaxCultureSummaryLength)
        {
            throw new ArgumentException($"Culture summary cannot exceed {MaxCultureSummaryLength} characters.", nameof(cultureSummary));
        }

        if (dispositionReason is not null && dispositionReason.Length > MaxDispositionReasonLength)
        {
            throw new ArgumentException($"Disposition reason cannot exceed {MaxDispositionReasonLength} characters.", nameof(dispositionReason));
        }

        if (score.HasValue && (score.Value < 0.0m || score.Value > 10.0m))
        {
            throw new ArgumentException("Score must be between 0.0 and 10.0.", nameof(score));
        }

        var actualId = id ?? Guid.CreateVersion7();
        if (duplicateOfId.HasValue && duplicateOfId.Value == actualId)
        {
            throw new ArgumentException("A posting cannot be a duplicate of itself.", nameof(duplicateOfId));
        }

        var timestamp = now ?? DateTimeOffset.UtcNow;
        var normalizedTitle = TitleNormalizer.Normalize(trimmedTitle);
        var normalizedUrl = UrlNormalizer.Normalize(originalUrl);

        var trimmedAtsTenant = string.IsNullOrWhiteSpace(atsTenant) ? null : atsTenant.Trim();
        var trimmedReqId = string.IsNullOrWhiteSpace(requisitionId) ? null : requisitionId.Trim();

        string? dedupKeyA = null;
        if (!string.IsNullOrWhiteSpace(trimmedAtsTenant) && !string.IsNullOrWhiteSpace(trimmedReqId))
        {
            dedupKeyA = $"{trimmedAtsTenant}|{trimmedReqId}";
        }

        string? dedupKeyB = normalizedUrl;

        string? dedupKeyC = null;
        if (string.IsNullOrWhiteSpace(trimmedAtsTenant) && !string.IsNullOrWhiteSpace(trimmedReqId))
        {
            dedupKeyC = $"{companyId:D}|{trimmedReqId}";
        }

        DateTimeOffset? initialDecidedAt = decidedAt;
        if (initialDecidedAt is null && (disposition == PostingDisposition.ScreenedOut || disposition == PostingDisposition.Closed || disposition == PostingDisposition.Rejected))
        {
            initialDecidedAt = timestamp;
        }

        return new Posting
        {
            Id = actualId,
            OwnerId = ownerId,
            CompanyId = companyId,
            Title = trimmedTitle,
            NormalizedTitle = normalizedTitle,
            OriginalUrl = originalUrl,
            NormalizedUrl = normalizedUrl,
            SourceKind = sourceKind,
            AtsTenant = trimmedAtsTenant,
            RequisitionId = trimmedReqId,
            DedupKeyA = dedupKeyA,
            DedupKeyB = dedupKeyB,
            DedupKeyC = dedupKeyC,
            Location = location?.Trim(),
            RemoteType = remoteType,
            Compensation = compensation ?? new(),
            PostedDate = postedDate,
            FirstSeenAt = timestamp,
            LastVerifiedAt = timestamp,
            Score = score,
            CultureLevel = cultureLevel,
            CultureSummary = cultureSummary?.Trim(),
            Disposition = disposition,
            DispositionReason = dispositionReason?.Trim(),
            DispositionCriterionId = dispositionCriterionId,
            ResurfacePolicy = resurfacePolicy,
            DecidedAt = initialDecidedAt,
            DuplicateOfId = duplicateOfId,
            ScreeningNoteId = screeningNoteId,
            VersionToken = Guid.CreateVersion7(),
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    public static Posting Rehydrate(
        Guid id,
        string ownerId,
        Guid companyId,
        string title,
        string normalizedTitle,
        string? originalUrl,
        string? normalizedUrl,
        SourceKind sourceKind,
        string? atsTenant,
        string? requisitionId,
        string? dedupKeyA,
        string? dedupKeyB,
        string? dedupKeyC,
        string? location,
        RemoteType remoteType,
        Compensation compensation,
        string? postedDate,
        DateTimeOffset firstSeenAt,
        DateTimeOffset lastVerifiedAt,
        string? closedDate,
        decimal? score,
        CultureLevel cultureLevel,
        string? cultureSummary,
        PostingDisposition disposition,
        string? dispositionReason,
        Guid? dispositionCriterionId,
        ResurfacePolicy resurfacePolicy,
        DateTimeOffset? decidedAt,
        Guid? duplicateOfId,
        Guid? screeningNoteId,
        Guid versionToken,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        return new Posting
        {
            Id = id,
            OwnerId = ownerId,
            CompanyId = companyId,
            Title = title,
            NormalizedTitle = normalizedTitle,
            OriginalUrl = originalUrl,
            NormalizedUrl = normalizedUrl,
            SourceKind = sourceKind,
            AtsTenant = atsTenant,
            RequisitionId = requisitionId,
            DedupKeyA = dedupKeyA,
            DedupKeyB = dedupKeyB,
            DedupKeyC = dedupKeyC,
            Location = location,
            RemoteType = remoteType,
            Compensation = compensation,
            PostedDate = postedDate,
            FirstSeenAt = firstSeenAt,
            LastVerifiedAt = lastVerifiedAt,
            ClosedDate = closedDate,
            Score = score,
            CultureLevel = cultureLevel,
            CultureSummary = cultureSummary,
            Disposition = disposition,
            DispositionReason = dispositionReason,
            DispositionCriterionId = dispositionCriterionId,
            ResurfacePolicy = resurfacePolicy,
            DecidedAt = decidedAt,
            DuplicateOfId = duplicateOfId,
            ScreeningNoteId = screeningNoteId,
            VersionToken = versionToken,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void UpdateLastVerified(DateTimeOffset timestamp)
    {
        LastVerifiedAt = timestamp;
        UpdatedAt = timestamp;
        VersionToken = Guid.CreateVersion7();
    }

    public void UpgradeIdentifiers(string? atsTenant, string? requisitionId, DateTimeOffset timestamp)
    {
        var trimmedAtsTenant = string.IsNullOrWhiteSpace(atsTenant) ? AtsTenant : atsTenant.Trim();
        var trimmedReqId = string.IsNullOrWhiteSpace(requisitionId) ? RequisitionId : requisitionId.Trim();

        AtsTenant = trimmedAtsTenant;
        RequisitionId = trimmedReqId;

        if (!string.IsNullOrWhiteSpace(trimmedAtsTenant) && !string.IsNullOrWhiteSpace(trimmedReqId))
        {
            DedupKeyA = $"{trimmedAtsTenant}|{trimmedReqId}";
            DedupKeyC = null;
        }
        else if (string.IsNullOrWhiteSpace(trimmedAtsTenant) && !string.IsNullOrWhiteSpace(trimmedReqId))
        {
            DedupKeyA = null;
            DedupKeyC = $"{CompanyId:D}|{trimmedReqId}";
        }

        UpdatedAt = timestamp;
        VersionToken = Guid.CreateVersion7();
    }

    public void Transition(
        PostingDisposition targetDisposition,
        string? reason,
        Guid? criterionId,
        ResurfacePolicy resurfacePolicy,
        DateTimeOffset timestamp)
    {
        Disposition = targetDisposition;
        DispositionReason = reason?.Trim();
        DispositionCriterionId = criterionId;
        ResurfacePolicy = resurfacePolicy;

        if (targetDisposition == PostingDisposition.ScreenedOut ||
            targetDisposition == PostingDisposition.Closed ||
            targetDisposition == PostingDisposition.Rejected)
        {
            DecidedAt = timestamp;
        }
        else if (targetDisposition == PostingDisposition.Lead)
        {
            DecidedAt = null;
        }

        UpdatedAt = timestamp;
        VersionToken = Guid.CreateVersion7();
    }

    public void TransitionToApplied(DateTimeOffset timestamp)
    {
        Disposition = PostingDisposition.Applied;
        DecidedAt = null;
        UpdatedAt = timestamp;
        VersionToken = Guid.CreateVersion7();
    }

    public void SynchronizeFromApplicationEvent(
        PostingDisposition terminalDisposition,
        string? reason,
        DateTimeOffset timestamp)
    {
        Disposition = terminalDisposition;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var trimmed = reason.Trim();
            DispositionReason = trimmed.Length > MaxDispositionReasonLength
                ? trimmed[..MaxDispositionReasonLength]
                : trimmed;
        }

        DecidedAt = timestamp;
        UpdatedAt = timestamp;
        VersionToken = Guid.CreateVersion7();
    }

    public List<string> ApplyExplicitUpdate(
        Guid? companyId,
        string? title,
        string? originalUrl,
        SourceKind? sourceKind,
        string? atsTenant,
        string? requisitionId,
        string? location,
        RemoteType? remoteType,
        Compensation? compensation,
        string? postedDate,
        decimal? score,
        CultureLevel? cultureLevel,
        string? cultureSummary,
        Guid? duplicateOfId,
        Guid? screeningNoteId,
        DateTimeOffset timestamp)
    {
        var changedFields = new List<string>();

        if (companyId.HasValue && companyId.Value != CompanyId)
        {
            CompanyId = companyId.Value;
            changedFields.Add("company_id");
        }

        if (!string.IsNullOrWhiteSpace(title) && title.Trim() != Title)
        {
            Title = title.Trim();
            NormalizedTitle = TitleNormalizer.Normalize(Title);
            changedFields.Add("title");
        }

        if (originalUrl is not null && originalUrl != OriginalUrl)
        {
            OriginalUrl = originalUrl;
            NormalizedUrl = UrlNormalizer.Normalize(originalUrl);
            DedupKeyB = NormalizedUrl;
            changedFields.Add("url");
        }

        if (sourceKind.HasValue && sourceKind.Value != SourceKind)
        {
            SourceKind = sourceKind.Value;
            changedFields.Add("source_kind");
        }

        if (atsTenant is not null && atsTenant != AtsTenant)
        {
            AtsTenant = atsTenant.Trim();
            changedFields.Add("ats_tenant");
        }

        if (requisitionId is not null && requisitionId != RequisitionId)
        {
            RequisitionId = requisitionId.Trim();
            changedFields.Add("requisition_id");
        }

        // Recompute dedup keys if tenant or req id changed
        if (changedFields.Contains("ats_tenant") || changedFields.Contains("requisition_id") || changedFields.Contains("company_id"))
        {
            if (!string.IsNullOrWhiteSpace(AtsTenant) && !string.IsNullOrWhiteSpace(RequisitionId))
            {
                DedupKeyA = $"{AtsTenant}|{RequisitionId}";
                DedupKeyC = null;
            }
            else if (string.IsNullOrWhiteSpace(AtsTenant) && !string.IsNullOrWhiteSpace(RequisitionId))
            {
                DedupKeyA = null;
                DedupKeyC = $"{CompanyId:D}|{RequisitionId}";
            }
            else
            {
                DedupKeyA = null;
                DedupKeyC = null;
            }
        }

        if (location is not null && location != Location)
        {
            Location = location.Trim();
            changedFields.Add("location");
        }

        if (remoteType.HasValue && remoteType.Value != RemoteType)
        {
            RemoteType = remoteType.Value;
            changedFields.Add("remote_type");
        }

        if (compensation is not null && compensation != Compensation)
        {
            Compensation = compensation;
            changedFields.Add("compensation");
        }

        if (postedDate is not null && postedDate != PostedDate)
        {
            PostedDate = postedDate;
            changedFields.Add("posted_date");
        }

        if (score.HasValue && score.Value != Score)
        {
            if (score.Value < 0.0m || score.Value > 10.0m)
            {
                throw new ArgumentException("Score must be between 0.0 and 10.0.", nameof(score));
            }
            Score = score.Value;
            changedFields.Add("score");
        }

        if (cultureLevel.HasValue && cultureLevel.Value != CultureLevel)
        {
            CultureLevel = cultureLevel.Value;
            changedFields.Add("culture_level");
        }

        if (cultureSummary is not null && cultureSummary != CultureSummary)
        {
            CultureSummary = cultureSummary.Trim();
            changedFields.Add("culture_summary");
        }

        if (duplicateOfId.HasValue && duplicateOfId.Value != DuplicateOfId)
        {
            if (duplicateOfId.Value == Id)
            {
                throw new ArgumentException("A posting cannot be a duplicate of itself.", nameof(duplicateOfId));
            }
            DuplicateOfId = duplicateOfId.Value;
            changedFields.Add("duplicate_of_id");
        }

        if (screeningNoteId.HasValue && screeningNoteId.Value != ScreeningNoteId)
        {
            ScreeningNoteId = screeningNoteId.Value;
            changedFields.Add("screening_note_id");
        }

        if (changedFields.Count > 0)
        {
            UpdatedAt = timestamp;
            VersionToken = Guid.CreateVersion7();
        }

        return changedFields;
    }
}
